using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using Xunit;

namespace PIYA_API.Tests.Unit;

public sealed class PdfExportServiceTests
{
    [Theory]
    [InlineData("prescription", "Medical Prescription")]
    [InlineData("doctor-note", "Medical Certificate")]
    [InlineData("appointment", "Appointment Confirmation")]
    [InlineData("patient-summary", "Patient Medical Summary")]
    [InlineData("referral", "Medical Referral Letter")]
    public async Task EveryExportProducesReadablePdfWithEmbeddedFonts(string kind, string title)
    {
        await using var db = new PharmacyApiDbContext(new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var ids = await Seed(db);
        var service = new PdfExportService(db, Mock.Of<ILogger<PdfExportService>>());
        var bytes = kind switch {
            "prescription" => await service.GeneratePrescriptionPdfAsync(ids.Prescription),
            "doctor-note" => await service.GenerateDoctorNotePdfAsync(ids.Note),
            "appointment" => await service.GenerateAppointmentConfirmationPdfAsync(ids.Appointment),
            "patient-summary" => await service.GeneratePatientMedicalSummaryPdfAsync(ids.Patient),
            "referral" => await service.GenerateReferralLetterPdfAsync(ids.Referral, includeClinicalNotes: false),
            _ => throw new InvalidOperationException("Unknown test export.")
        };
        using var stream = new MemoryStream(bytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(title, document.Info.Title);
        Assert.InRange(document.PageCount, 1, 2);
        Assert.All(document.Pages.Cast<PdfPage>(), page => Assert.True(page.Contents.Elements.Count > 0));
        Assert.Contains(document.Internals.GetAllObjects().OfType<PdfDictionary>(),
            dictionary => dictionary.Elements.ContainsKey("/FontFile2"));

        // An explicit opt-in writes only synthetic fixtures for independent
        // extraction/rendering; ordinary test runs need no external PDF tools.
        var reviewDirectory = Environment.GetEnvironmentVariable("PIYA_PDF_REVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(reviewDirectory))
        {
            Directory.CreateDirectory(reviewDirectory);
            await File.WriteAllBytesAsync(Path.Combine(reviewDirectory, kind + ".pdf"), bytes);
        }
    }

    [Theory]
    [InlineData(XFontStyleEx.Regular)]
    [InlineData(XFontStyleEx.Bold)]
    [InlineData(XFontStyleEx.Italic)]
    [InlineData(XFontStyleEx.BoldItalic)]
    public void EveryBundledFaceHasAzerbaijaniGlyphs(XFontStyleEx style)
    {
        PiyaPdfFontResolver.EnsureConfigured();
        var font = new XFont(PiyaPdfFontResolver.Family, 12, style);
        foreach (var character in "ƏəÇçĞğİıÖöŞşÜü")
            Assert.NotEqual((ushort)0, GlyphHelper.GlyphIndexFromCodePoint(character, font));
    }

    private static async Task<(Guid Patient, Guid Prescription, Guid Note, Guid Appointment, Guid Referral)> Seed(PharmacyApiDbContext db)
    {
        var now = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);
        User Person(UserRole role, string first, string last) => new() {
            Id = Guid.NewGuid(), Username = Guid.NewGuid().ToString("N"), FirstName = first, LastName = last,
            Email = "synthetic@example.test", PhoneNumber = "+994000000000", Role = role,
            DateOfBirth = new DateTime(1990, 1, 2, 0, 0, 0, DateTimeKind.Utc), IsActive = true };
        var patient = Person(UserRole.Patient, "Əli", "ÇĞİıÖŞÜ əçğıöşü");
        var doctor = Person(UserRole.Doctor, "Məmməd", "Şirin");
        var hospital = new Hospital { Id = Guid.NewGuid(), Name = "Synthetic Bakı Clinic", Address = "Synthetic test address",
            City = "Bakı", Country = "AZ", PhoneNumber = "+994000000001", IsActive = true };
        var appointment = new Appointment { Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id,
            HospitalId = hospital.Id, ScheduledAt = now.AddDays(1), DurationMinutes = 30, Reason = "Synthetic follow-up" };
        var medication = new Medication { Id = Guid.NewGuid(), BrandName = "Synthetic tablet", GenericName = "Synthetic compound", Form = "Tablet", Strength = "10 mg" };
        var prescription = new Prescription { Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id,
            IssuedAt = now, ExpiresAt = now.AddDays(30), Diagnosis = "Synthetic review", Instructions = "Synthetic instructions" };
        prescription.Items.Add(new PrescriptionItem { Id = Guid.NewGuid(), PrescriptionId = prescription.Id, MedicationId = medication.Id,
            Dosage = "10 mg", Frequency = "Once daily", Duration = "7 days", Quantity = 7, Instructions = "Synthetic only" });
        var note = new DoctorNote { Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id, Title = "Synthetic certificate",
            NoteNumber = "SYNTHETIC-001", PublicTokenHash = "synthetic", Summary = "PRIVATE NOTE SUMMARY", IncludeSummaryInPublicView = false,
            IssuedAt = now, ValidFrom = now, ValidTo = now.AddDays(2) };
        var referral = new Referral { Id = Guid.NewGuid(), PatientId = patient.Id, ReferringDoctorId = doctor.Id,
            Reason = "Synthetic referral review", ClinicalNotes = "PRIVATE REFERRAL NOTES", IsExternal = true,
            ExternalProviderName = "Synthetic specialist", ExternalProviderContact = "synthetic@example.test", CreatedAt = now };
        db.Users.AddRange(patient, doctor); db.Hospitals.Add(hospital); db.Appointments.Add(appointment);
        db.Medications.Add(medication); db.Prescriptions.Add(prescription); db.DoctorNotes.Add(note); db.Referrals.Add(referral);
        db.DoctorProfiles.Add(new DoctorProfile { Id = Guid.NewGuid(), UserId = doctor.Id, LicenseNumber = "SYNTHETIC-ONLY",
            LicenseExpiryDate = now.AddYears(1), HospitalIds = [hospital.Id] });
        await db.SaveChangesAsync();
        return (patient.Id, prescription.Id, note.Id, appointment.Id, referral.Id);
    }
}
