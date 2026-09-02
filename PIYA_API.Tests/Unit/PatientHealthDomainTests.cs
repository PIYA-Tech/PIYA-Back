using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public sealed class PatientHealthDomainTests
{
    [Fact]
    public async Task RecordingDose_WithSameClientEvent_DeductsSupplyOnlyOnce()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient);
        db.Users.Add(patient);
        var medication = MakeTrackedMedication(patient.Id, 10);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku"));
        var doseLocal = localNow.AddMinutes(-5);
        var schedule = new MedicationDoseSchedule
        {
            Id = Guid.NewGuid(), PatientMedicationId = medication.Id,
            DoseAmount = 1, TimeOfDayMinutes = doseLocal.Hour * 60 + doseLocal.Minute,
            DaysOfWeek = MedicationScheduleDays.EveryDay, TimeZoneId = "Asia/Baku",
            GracePeriodMinutes = 120
        };
        db.Set<PatientMedication>().Add(medication);
        db.Set<MedicationDoseSchedule>().Add(schedule);
        await db.SaveChangesAsync();
        var scheduledFor = MedicationScheduleClock.OccurrenceOnDate(
            medication, schedule, DateOnly.FromDateTime(doseLocal))!.Value;
        var clientEventId = Guid.NewGuid();
        var service = new PatientMedicationService(db);

        var first = await service.RecordDoseAsync(patient.Id, new RecordMedicationDoseRequest
        {
            ScheduleId = schedule.Id, ScheduledFor = scheduledFor,
            Status = MedicationDoseStatus.Taken, ClientEventId = clientEventId
        });
        var retry = await service.RecordDoseAsync(patient.Id, new RecordMedicationDoseRequest
        {
            ScheduleId = schedule.Id, ScheduledFor = scheduledFor,
            Status = MedicationDoseStatus.Taken, ClientEventId = clientEventId
        });

        retry.Id.Should().Be(first.Id);
        (await db.Set<PatientMedication>().FindAsync(medication.Id))!.SupplyRemaining.Should().Be(9);
        (await db.Set<MedicationDoseOccurrence>().CountAsync()).Should().Be(1);
        var history = await service.GetDoseHistoryAsync(
            patient.Id, scheduledFor.AddHours(-1), scheduledFor.AddHours(1));
        history.Taken.Should().Be(1);
        history.Items.Single().MedicationName.Should().Be("DemoMed");
    }

    [Fact]
    public async Task MedicationAndInboxQueries_AreRestrictedToTheirOwner()
    {
        await using var db = CreateContext();
        var owner = MakeUser(UserRole.Patient);
        var stranger = MakeUser(UserRole.Patient);
        db.Users.AddRange(owner, stranger);
        var medication = MakeTrackedMedication(owner.Id, 10);
        db.Set<PatientMedication>().Add(medication);
        await db.SaveChangesAsync();
        var medicationService = new PatientMedicationService(db);
        var inbox = new PatientNotificationInboxService(db);
        var notification = await inbox.EnqueueAsync(
            owner.Id, PatientNotificationCategory.System, "Private", "Owner only");

        (await medicationService.GetAsync(stranger.Id, medication.Id)).Should().BeNull();
        (await inbox.GetPageAsync(stranger.Id, 1, 30, false)).Items.Should().BeEmpty();
        (await inbox.MarkReadAsync(stranger.Id, notification.Id)).Should().BeFalse();
        (await inbox.MarkReadAsync(owner.Id, notification.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ReminderProcessor_CreatesDurableInboxItem_AndAdvancesSchedule()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient);
        db.Users.Add(patient);
        var medication = MakeTrackedMedication(patient.Id, 10);
        var now = DateTime.UtcNow;
        var schedule = new MedicationDoseSchedule
        {
            Id = Guid.NewGuid(), PatientMedicationId = medication.Id,
            DoseAmount = 1, TimeOfDayMinutes = 8 * 60,
            DaysOfWeek = MedicationScheduleDays.EveryDay, TimeZoneId = "Asia/Baku",
            GracePeriodMinutes = 120, NextReminderAt = now.AddMinutes(-1)
        };
        db.Set<PatientMedication>().Add(medication);
        db.Set<MedicationDoseSchedule>().Add(schedule);
        await db.SaveChangesAsync();
        var push = new Mock<INotificationService>();
        push.Setup(item => item.SendPushNotificationAsync(
                patient.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(true);
        var processor = new MedicationReminderProcessor(
            db, new PatientNotificationInboxService(db), push.Object,
            Mock.Of<ILogger<MedicationReminderProcessor>>());

        var result = await processor.ProcessDueAsync(now);

        result.RemindersCreated.Should().Be(1);
        (await db.Set<MedicationDoseOccurrence>().SingleAsync()).Status
            .Should().Be(MedicationDoseStatus.ReminderSent);
        (await db.Set<PatientInboxNotification>().SingleAsync()).UserId.Should().Be(patient.Id);
        (await db.Set<MedicationDoseSchedule>().FindAsync(schedule.Id))!.NextReminderAt
            .Should().BeAfter(now);
        push.Verify(item => item.SendPushNotificationAsync(
            patient.Id, "Medication reminder", "It is time for a scheduled medication.",
            It.IsAny<Dictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task StructuredLabResults_CalculateFlags_AndRemainPatientOwned()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient);
        var stranger = MakeUser(UserRole.Patient);
        var doctor = MakeUser(UserRole.Doctor);
        db.Users.AddRange(patient, stranger, doctor);
        var test = new MedicalTest
        {
            Id = Guid.NewGuid(), PatientId = patient.Id, OrderedByDoctorId = doctor.Id,
            PerformedByDoctorId = doctor.Id, TestType = MedicalTestType.BloodPanel,
            Status = MedicalTestStatus.InProgress
        };
        db.MedicalTests.Add(test);
        await db.SaveChangesAsync();
        var service = new StructuredLabResultService(
            db, new PatientNotificationInboxService(db), Mock.Of<INotificationService>(),
            Mock.Of<IAuditService>(), Mock.Of<ILogger<StructuredLabResultService>>());

        var report = await service.ReplaceAnalytesAsync(test.Id, doctor.Id, false,
            new ReplaceLabAnalytesRequest
            {
                Analytes =
                [
                    new LabAnalyteInput
                    {
                        Code = "hgb", Name = "Haemoglobin", NumericValue = 9.8m,
                        Unit = "g/dL", ReferenceLow = 12m, ReferenceHigh = 16m
                    }
                ]
            });

        report.Status.Should().Be(MedicalTestStatus.ResultsReady);
        report.Analytes.Single().Flag.Should().Be(LabAnalyteFlag.Low);
        (await service.GetPatientReportAsync(stranger.Id, test.Id)).Should().BeNull();
        (await service.GetPatientReportAsync(patient.Id, test.Id))!.Analytes.Should().HaveCount(1);
    }

    [Fact]
    public async Task CollectingReadyRefill_LinksPickupPrescriptionAndTrackedSupply()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient);
        var doctor = MakeUser(UserRole.Doctor);
        var pharmacist = MakeUser(UserRole.Pharmacist);
        var company = new PharmacyCompany { Id = Guid.NewGuid(), Name = "Network" };
        var pharmacy = new Pharmacy
        {
            Id = Guid.NewGuid(), Name = "PIYA Pharmacy", Address = "Baku", City = "Baku",
            Country = "Azerbaijan", IsActive = true, Company = company,
            Coordinates = new Coordinates { Id = Guid.NewGuid(), Latitude = 40.4, Longitude = 49.8 }
        };
        var catalogueMedication = new Medication
        {
            Id = Guid.NewGuid(), BrandName = "DemoMed", GenericName = "Demo",
            Form = "Tablet", Strength = "10 mg"
        };
        var prescription = new Prescription
        {
            Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id,
            Patient = patient, Doctor = doctor, Status = PrescriptionStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        var item = new PrescriptionItem
        {
            Id = Guid.NewGuid(), PrescriptionId = prescription.Id, Prescription = prescription,
            MedicationId = catalogueMedication.Id, Medication = catalogueMedication,
            Dosage = "10 mg", Frequency = "Daily", Duration = "30 days", Quantity = 30
        };
        prescription.Items.Add(item);
        var refill = new PatientRefillRequest
        {
            Id = Guid.NewGuid(), PatientId = patient.Id, PrescriptionId = prescription.Id,
            PrescriptionItemId = item.Id, PharmacyId = pharmacy.Id,
            Status = PatientRefillRequestStatus.Ready
        };
        var tracked = MakeTrackedMedication(patient.Id, 0);
        tracked.PrescriptionItemId = item.Id;
        tracked.MedicationId = catalogueMedication.Id;
        tracked.Source = PatientMedicationSource.Prescription;
        db.AddRange(patient, doctor, pharmacist, company, pharmacy, catalogueMedication, prescription, refill);
        db.Set<PatientMedication>().Add(tracked);
        await db.SaveChangesAsync();
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(value => value.IsStaffAtPharmacyAsync(pharmacy.Id, pharmacist.Id)).ReturnsAsync(true);
        var service = new PatientPickupService(
            db, staff.Object, new PatientNotificationInboxService(db),
            Mock.Of<INotificationService>(), Mock.Of<IAuditService>(),
            Mock.Of<ILogger<PatientPickupService>>());

        var response = await service.CollectAsync(refill.Id, pharmacist.Id, false, 30);

        response.QuantityCollected.Should().Be(30);
        refill.Status.Should().Be(PatientRefillRequestStatus.Collected);
        item.IsFulfilled.Should().BeTrue();
        tracked.SupplyRemaining.Should().Be(30);
        (await db.Set<PharmacyPickup>().CountAsync()).Should().Be(1);
        (await db.Set<PatientRefillStatusEvent>().SingleAsync()).Status
            .Should().Be(PatientRefillRequestStatus.Collected);
    }

    private static PatientHealthTestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new PatientHealthTestDbContext(options);
    }

    private static PatientMedication MakeTrackedMedication(Guid patientId, decimal supply) => new()
    {
        Id = Guid.NewGuid(), PatientId = patientId, Source = PatientMedicationSource.PatientEntered,
        DisplayName = "DemoMed", Dosage = "1 tablet", StartDate = DateTime.UtcNow.AddDays(-10),
        SupplyTotal = supply, SupplyRemaining = supply, SupplyUnit = "tablet",
        LowSupplyThreshold = 2
    };

    private static User MakeUser(UserRole role) => new()
    {
        Id = Guid.NewGuid(), Username = Guid.NewGuid().ToString("N"), PasswordHash = "hash",
        FirstName = role.ToString(), LastName = "User", Email = $"{Guid.NewGuid():N}@example.com",
        PhoneNumber = "+994501234567", Role = role, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private sealed class PatientHealthTestDbContext(DbContextOptions<PharmacyApiDbContext> options)
        : PharmacyApiDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePatientHealthDomain();
        }
    }
}
