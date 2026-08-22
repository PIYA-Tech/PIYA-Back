using Microsoft.EntityFrameworkCore;
using Npgsql;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Seeds demo/development data so the app works out of the box.
/// All operations are idempotent — safe to run on every startup.
/// Medications are imported from the Azerbaijan Pharmaceutical Registry on first run.
/// </summary>
public static class DataSeeder
{
    private const string DemoPassword = "Test@1234";
    private const string DemoHash = "$2a$11$MM/G.xABJSIlt/lfqsiu9O8/wKvF4sk31Pl9tL7YJ.tEu0Gpp6gR6";

    private static readonly (string Username, string First, string Last, string Email, string Phone, UserRole Role)[] DemoUsers =
    [
        ("mahammad_babayev", "Mahammad", "Babayev", "mahammad_babayev@piya.dev", "+994501000001", UserRole.Patient),
        ("dr_at_piya",       "Dr",       "Piya",    "dr_at_piya@piya.dev",       "+994501000002", UserRole.Doctor),
        ("pharma_piya",      "Pharma",   "Piya",    "pharma_piya@piya.dev",      "+994501000003", UserRole.Pharmacist),
        ("admin_piya",       "Admin",    "PIYA",    "admin_piya@piya.dev",        "+994501000004", UserRole.Admin),
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db     = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        // ── Users ──────────────────────────────────────────────────────────────
        // Batch-preload all demo users matched by either username or email
        // so that rows created with an old email are still found and reconciled.
        var demoEmails    = DemoUsers.Select(u => u.Email).ToArray();
        var demoUsernames = DemoUsers.Select(u => u.Username).ToArray();

        var existing = await db.Users
            .Where(u => demoEmails.Contains(u.Email) || demoUsernames.Contains(u.Username))
            .ToDictionaryAsync(u => u.Username);

        foreach (var (username, first, last, email, phone, role) in DemoUsers)
        {
            if (!existing.TryGetValue(username, out var user))
            {
                // Not in DB yet — create and track in both EF and our local dict.
                user = new User
                {
                    Id              = Guid.NewGuid(),
                    Email           = email,
                    Username        = username,
                    FirstName       = first,
                    LastName        = last,
                    PhoneNumber     = phone,
                    Role            = role,
                    PasswordHash    = DemoHash,
                    DateOfBirth     = new DateTime(1990, 1, 1),
                    IsActive        = true,
                    IsEmailVerified = true,
                    IsPhoneVerified = true,
                    CreatedAt       = DateTime.UtcNow,
                    UpdatedAt       = DateTime.UtcNow,
                };
                db.Users.Add(user);
                existing[username] = user;
            }

            // Always reconcile ALL mutable fields — including email — so repeated
            // runs correct rows that were created with an old/different email.
            user.Username    = username;
            user.Email       = email;
            user.FirstName   = first;
            user.LastName    = last;
            user.PhoneNumber = phone;
            user.Role        = role;
            user.IsActive        = true;
            user.IsEmailVerified = true;
            user.UpdatedAt   = DateTime.UtcNow;

            if (!hasher.VerifyPassword(DemoPassword, user.PasswordHash))
                user.PasswordHash = DemoHash;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            // Another instance seeded concurrently (e.g. parallel CI jobs).
            // The row already exists — swallow and continue.
            logger.LogWarning(ex,
                "[DataSeeder] Unique-constraint conflict while seeding demo users — " +
                "a concurrent startup already inserted the row. Continuing.");
        }

        // ── Medications from Azerbaijan Pharmaceutical Registry ─────────────────
        // Only runs when the DB is empty — subsequent startups skip this entirely.
        var count = await db.Medications.CountAsync();
        if (count == 0)
        {
            logger.LogInformation("[DataSeeder] No medications found — importing from Azerbaijan Pharmaceutical Registry...");
            try
            {
                var registryService = scope.ServiceProvider
                    .GetRequiredService<IAzerbaijanPharmaceuticalRegistryService>();

                var result = await registryService.SyncMedicationsAsync();
                if (result.Success)
                    logger.LogInformation("[DataSeeder] Registry sync complete — {Total} records imported ({New} new)",
                        result.TotalRecords, result.NewRecords);
                else
                    logger.LogWarning("[DataSeeder] Registry sync failed: {Errors}",
                        string.Join("; ", result.Errors));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DataSeeder] Registry sync threw an exception");
            }
        }
        else
        {
            logger.LogInformation("[DataSeeder] {Count} medications already in DB — skipping registry sync", count);
        }

        // ── Fictional conference workflow ────────────────────────────────────
        // Explicitly labelled demo entities make a non-production build useful
        // without representing invented data as a real Baku provider.
        var patient = existing["mahammad_babayev"];
        var doctor = existing["dr_at_piya"];
        var pharmacist = existing["pharma_piya"];

        var hospital = await db.Hospitals.FirstOrDefaultAsync(item =>
            item.Name == "PIYA Demo Clinic — Fictional");
        if (hospital is null)
        {
            hospital = new Hospital
            {
                Id = Guid.NewGuid(), Name = "PIYA Demo Clinic — Fictional",
                Address = "Conference demonstration, Baku", City = "Baku",
                Country = "Azerbaijan", PhoneNumber = "+994120000000",
                Email = "demo-clinic@piya.dev", EmergencyContact = "+994120000000",
                Departments = ["General Practice", "Cardiology"], IsActive = true,
                Coordinates = new Coordinates { Latitude = 40.4093, Longitude = 49.8671 },
                OperatingHours = "{\"Monday\":\"09:00-18:00\",\"Tuesday\":\"09:00-18:00\"}"
            };
            db.Hospitals.Add(hospital);
        }

        var company = await db.PharmacyCompanies.FirstOrDefaultAsync(item =>
            item.Name == "PIYA Conference Demo Network (Fictional)");
        if (company is null)
        {
            company = new PharmacyCompany
            {
                Id = Guid.NewGuid(), Name = "PIYA Conference Demo Network (Fictional)"
            };
            db.PharmacyCompanies.Add(company);
        }

        var pharmacy = await db.Pharmacies.FirstOrDefaultAsync(item =>
            item.Name == "PIYA Demo Pharmacy — Fictional");
        if (pharmacy is null)
        {
            pharmacy = new Pharmacy
            {
                Id = Guid.NewGuid(), Name = "PIYA Demo Pharmacy — Fictional",
                Address = "Conference demonstration, Baku", City = "Baku",
                Country = "Azerbaijan", PhoneNumber = "+994120000001",
                Email = "demo-pharmacy@piya.dev", IsActive = true, Is24Hours = true,
                Services = ["Prescription Filling", "Consultation"],
                Coordinates = new Coordinates { Latitude = 40.4110, Longitude = 49.8700 },
                Company = company
            };
            db.Pharmacies.Add(pharmacy);
        }

        if (!await db.DoctorProfiles.AnyAsync(item => item.UserId == doctor.Id))
        {
            db.DoctorProfiles.Add(new DoctorProfile
            {
                Id = Guid.NewGuid(), UserId = doctor.Id, User = doctor,
                LicenseNumber = "DEMO-NOT-A-REAL-LICENSE", LicenseAuthority = "PIYA Demo",
                LicenseExpiryDate = DateTime.UtcNow.AddYears(1),
                Specialization = MedicalSpecialization.GeneralPractice,
                YearsOfExperience = 10, Languages = ["Azerbaijani", "English", "Russian"],
                Biography = "Fictional conference demonstration profile.",
                ConsultationFee = 40, AcceptingNewPatients = true,
                HospitalIds = [hospital.Id],
                WorkingHours = "[{\"dayOfWeek\":\"Monday\",\"slots\":[{\"start\":\"09:00\",\"end\":\"18:00\"}]}]"
            });
        }

        if (!await db.PharmacistProfiles.AnyAsync(item => item.UserId == pharmacist.Id))
        {
            db.PharmacistProfiles.Add(new PharmacistProfile
            {
                Id = Guid.NewGuid(), UserId = pharmacist.Id, User = pharmacist,
                LicenseNumber = "DEMO-NOT-A-REAL-LICENSE", LicenseAuthority = "PIYA Demo",
                LicenseExpiryDate = DateTime.UtcNow.AddYears(1),
                LicenseStatus = PharmacistLicenseStatus.Active,
                PrimaryPharmacyId = pharmacy.Id, PrimaryPharmacy = pharmacy,
                Biography = "Fictional conference demonstration profile."
            });
        }

        if (!await db.PharmacyStaff.AnyAsync(item =>
                item.UserId == pharmacist.Id && item.PharmacyId == pharmacy.Id))
        {
            db.PharmacyStaff.Add(new PharmacyStaff
            {
                Id = Guid.NewGuid(), UserId = pharmacist.Id, User = pharmacist,
                PharmacyId = pharmacy.Id, Pharmacy = pharmacy,
                Role = PharmacyStaffRole.Staff, IsActive = true,
                Notes = "Fictional conference demonstration assignment."
            });
        }

        var medication = await db.Medications.FirstOrDefaultAsync(item =>
            item.BrandName == "PIYA DemoMed (Fictional)");
        if (medication is null)
        {
            medication = new Medication
            {
                Id = Guid.NewGuid(), BrandName = "PIYA DemoMed (Fictional)",
                GenericName = "Conference demonstration medicine", Form = "Tablet",
                Strength = "10 mg", ActiveIngredients = ["Demo ingredient"],
                Manufacturer = "PIYA Demo", RequiresPrescription = true,
                IsAvailable = true, Country = "Azerbaijan"
            };
            db.Medications.Add(medication);
        }

        if (!await db.PharmacyInventories.AnyAsync(item =>
                item.PharmacyId == pharmacy.Id && item.MedicationId == medication.Id))
        {
            db.PharmacyInventories.Add(new PharmacyInventory
            {
                Id = Guid.NewGuid(), PharmacyId = pharmacy.Id, Pharmacy = pharmacy,
                MedicationId = medication.Id, Medication = medication,
                QuantityInStock = 30, MinimumStockLevel = 5, ReorderQuantity = 20,
                Price = 12.50m, Currency = "AZN", BatchNumber = "DEMO-001",
                ExpirationDate = DateTime.UtcNow.AddYears(1), IsAvailable = true
            });
        }

        var demoAppointment = await db.Appointments.FirstOrDefaultAsync(item =>
            item.PatientId == patient.Id && item.DoctorId == doctor.Id &&
            item.Reason == "Fictional conference demonstration visit");
        if (demoAppointment is null)
        {
            demoAppointment = new Appointment
            {
                Id = Guid.NewGuid(), PatientId = patient.Id, Patient = patient,
                DoctorId = doctor.Id, Doctor = doctor, HospitalId = hospital.Id, Hospital = hospital,
                ScheduledAt = DateTime.UtcNow.Date.AddHours(13), DurationMinutes = 30,
                Status = AppointmentStatus.Confirmed,
                Reason = "Fictional conference demonstration visit"
            };
            db.Appointments.Add(demoAppointment);
        }

        if (!await db.Prescriptions.AnyAsync(item =>
                item.PatientId == patient.Id && item.Diagnosis == "Fictional demo diagnosis"))
        {
            db.Prescriptions.Add(new Prescription
            {
                Id = Guid.NewGuid(), PatientId = patient.Id, Patient = patient,
                DoctorId = doctor.Id, Doctor = doctor, AppointmentId = demoAppointment.Id,
                Appointment = demoAppointment, Status = PrescriptionStatus.Active,
                Diagnosis = "Fictional demo diagnosis",
                Instructions = "Conference demonstration only — not medical advice.",
                IssuedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(30),
                Items =
                [
                    new PrescriptionItem
                    {
                        Id = Guid.NewGuid(), MedicationId = medication.Id, Medication = medication,
                        Dosage = "10 mg", Frequency = "Once daily", Duration = "7 days",
                        Quantity = 7, Instructions = "Fictional conference demonstration only."
                    }
                ]
            });
        }

        if (!await db.EmergencyHealthProfiles.AnyAsync(item => item.PatientId == patient.Id))
        {
            db.EmergencyHealthProfiles.Add(new EmergencyHealthProfile
            {
                Id = Guid.NewGuid(), PatientId = patient.Id, Patient = patient,
                BloodType = "O+", Allergies = "Fictional demo allergy: penicillin",
                ChronicConditions = "Fictional demo condition: asthma",
                CurrentMedications = "PIYA DemoMed 10 mg once daily",
                EmergencyContacts = "Demo Contact, +994501000099",
                AdditionalNotes = "Conference demonstration data — not a real patient."
            });
        }

        await db.SaveChangesAsync();
        logger.LogInformation("[DataSeeder] Fictional Baku conference workflow is ready");
    }
}
