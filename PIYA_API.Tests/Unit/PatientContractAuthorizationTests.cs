using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Controllers;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class PatientContractAuthorizationTests
{
    [Fact]
    public void PublicDoctorContract_UsesExplicitIds_AndOmitsCredentialsAndAccountSecrets()
    {
        var user = MakeUser(UserRole.Doctor);
        var profile = new DoctorProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            LicenseNumber = "PRIVATE-LICENSE",
            LicenseAuthority = "PRIVATE-AUTHORITY",
            Specialization = MedicalSpecialization.Cardiology,
            YearsOfExperience = 8
        };

        var dto = PublicDoctorProfileResponseDto.FromEntity(profile);
        var json = JsonSerializer.Serialize(dto);

        dto.ProfileId.Should().Be(profile.Id);
        dto.DoctorUserId.Should().Be(user.Id);
        dto.Id.Should().Be(profile.Id);
        dto.UserId.Should().Be(user.Id);
        json.Should().NotContain("PRIVATE-LICENSE");
        json.Should().NotContain("PRIVATE-AUTHORITY");
        json.Should().NotContain(user.Email);
        json.Should().NotContain(user.PasswordHash);
    }

    [Fact]
    public async Task AppointmentBooking_AdminSuppliedPatient_IsHonored_AndReturnsBoundedDto()
    {
        var adminId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var appointments = new Mock<IAppointmentService>();
        appointments.Setup(service => service.BookAppointmentAsync(It.IsAny<Appointment>()))
            .ReturnsAsync((Appointment appointment) =>
            {
                appointment.Id = Guid.NewGuid();
                return appointment;
            });
        var controller = new AppointmentController(
            appointments.Object,
            Mock.Of<IUserService>(),
            Mock.Of<ILogger<AppointmentController>>());
        SetUser(controller, adminId, "Admin");

        var result = await controller.BookAppointment(new AppointmentRequest(
            patientId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(2),
            "Consultation"));

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.Value.Should().BeOfType<AppointmentResponseDto>()
            .Which.PatientId.Should().Be(patientId);
        appointments.Verify(service => service.BookAppointmentAsync(
            It.Is<Appointment>(appointment => appointment.PatientId == patientId)), Times.Once);
    }

    [Fact]
    public async Task MedicalTest_UnrelatedDoctor_CannotReadPatientTest()
    {
        var callerId = Guid.NewGuid();
        var testId = Guid.NewGuid();
        var tests = new Mock<IMedicalTestService>();
        tests.Setup(service => service.GetByIdAsync(testId)).ReturnsAsync(new MedicalTest
        {
            Id = testId,
            PatientId = Guid.NewGuid(),
            OrderedByDoctorId = Guid.NewGuid(),
            TestType = MedicalTestType.BloodPanel
        });
        var appointments = new Mock<IAppointmentService>();
        appointments.Setup(service => service.HasDoctorPatientRelationshipAsync(
            callerId, It.IsAny<Guid>())).ReturnsAsync(false);
        var controller = MakeMedicalTestController(tests, new Mock<IReferralService>(), appointments);
        SetUser(controller, callerId, "Doctor");

        var result = await controller.GetById(testId);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task MedicalTest_PatientResponse_HidesDocumentStorageInternals()
    {
        var patientId = Guid.NewGuid();
        var testId = Guid.NewGuid();
        var tests = new Mock<IMedicalTestService>();
        tests.Setup(service => service.GetByIdAsync(testId)).ReturnsAsync(new MedicalTest
        {
            Id = testId,
            PatientId = patientId,
            OrderedByDoctorId = Guid.NewGuid(),
            TestType = MedicalTestType.MRI,
            Documents =
            [
                new MedicalDocument
                {
                    Id = Guid.NewGuid(),
                    UserId = patientId,
                    UploadedByUserId = Guid.NewGuid(),
                    FileName = "result.pdf",
                    ContentType = "application/pdf",
                    ObjectKey = "private/bucket/key",
                    FileHash = "private-hash",
                    UploadedAt = DateTime.UtcNow
                }
            ]
        });
        var controller = MakeMedicalTestController(
            tests, new Mock<IReferralService>(), new Mock<IAppointmentService>());
        SetUser(controller, patientId, "Patient");

        var result = await controller.GetById(testId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<MedicalTestResponseDto>().Subject;
        dto.Documents.Should().ContainSingle();
        var json = JsonSerializer.Serialize(dto);
        json.Should().NotContain("private/bucket/key");
        json.Should().NotContain("private-hash");
        json.Should().NotContain("UploadedByUserId");
    }

    [Fact]
    public async Task MedicalTest_ReferralRoute_ChecksReferralPartyBeforeLoadingTests()
    {
        var referralId = Guid.NewGuid();
        var tests = new Mock<IMedicalTestService>();
        var referrals = new Mock<IReferralService>();
        referrals.Setup(service => service.GetByIdAsync(referralId)).ReturnsAsync(new Referral
        {
            Id = referralId,
            PatientId = Guid.NewGuid(),
            ReferringDoctorId = Guid.NewGuid(),
            ReferredToDoctorId = Guid.NewGuid(),
            ReferredToSpecialty = MedicalSpecialization.Neurology,
            Reason = "Assessment"
        });
        var controller = MakeMedicalTestController(tests, referrals, new Mock<IAppointmentService>());
        SetUser(controller, Guid.NewGuid(), "Patient");

        var result = await controller.GetByReferral(referralId);

        result.Should().BeOfType<ForbidResult>();
        tests.Verify(service => service.GetByReferralAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Referral_PatientResponse_ExcludesClinicalNotes()
    {
        var patientId = Guid.NewGuid();
        var referralId = Guid.NewGuid();
        var referrals = new Mock<IReferralService>();
        referrals.Setup(service => service.GetByIdAsync(referralId)).ReturnsAsync(new Referral
        {
            Id = referralId,
            PatientId = patientId,
            ReferringDoctorId = Guid.NewGuid(),
            ReferredToSpecialty = MedicalSpecialization.Cardiology,
            Reason = "Patient-visible reason",
            ClinicalNotes = "DOCTOR-ONLY"
        });
        var controller = MakeReferralController(referrals, new Mock<IPdfExportService>(), new Mock<IAppointmentService>());
        SetUser(controller, patientId, "Patient");

        var result = await controller.GetById(referralId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ReferralResponseDto>().Subject;
        dto.ClinicalNotes.Should().BeNull();
        JsonSerializer.Serialize(dto).Should().NotContain("ClinicalNotes");
    }

    [Fact]
    public async Task Referral_AvailableDoctors_IsRestrictedToReferralParties()
    {
        var referralId = Guid.NewGuid();
        var referrals = new Mock<IReferralService>();
        referrals.Setup(service => service.GetByIdAsync(referralId)).ReturnsAsync(new Referral
        {
            Id = referralId,
            PatientId = Guid.NewGuid(),
            ReferringDoctorId = Guid.NewGuid(),
            ReferredToSpecialty = MedicalSpecialization.Oncology,
            Reason = "Assessment"
        });
        var controller = MakeReferralController(referrals, new Mock<IPdfExportService>(), new Mock<IAppointmentService>());
        SetUser(controller, Guid.NewGuid(), "Patient");

        var result = await controller.GetAvailableDoctors(referralId);

        result.Should().BeOfType<ForbidResult>();
        referrals.Verify(service => service.GetAvailableDoctorsAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Referral_PatientPdf_ExplicitlyExcludesClinicalNotes()
    {
        var patientId = Guid.NewGuid();
        var referralId = Guid.NewGuid();
        var referrals = new Mock<IReferralService>();
        referrals.Setup(service => service.GetByIdAsync(referralId)).ReturnsAsync(new Referral
        {
            Id = referralId,
            PatientId = patientId,
            ReferringDoctorId = Guid.NewGuid(),
            ReferredToSpecialty = MedicalSpecialization.Cardiology,
            Reason = "Assessment",
            ClinicalNotes = "DOCTOR-ONLY"
        });
        var pdf = new Mock<IPdfExportService>();
        pdf.Setup(service => service.GenerateReferralLetterPdfAsync(referralId, false))
            .ReturnsAsync([1, 2, 3]);
        var controller = MakeReferralController(referrals, pdf, new Mock<IAppointmentService>());
        SetUser(controller, patientId, "Patient");

        var result = await controller.ExportPdf(referralId);

        result.Should().BeOfType<FileContentResult>();
        pdf.Verify(service => service.GenerateReferralLetterPdfAsync(referralId, false), Times.Once);
        pdf.Verify(service => service.GenerateReferralLetterPdfAsync(referralId, true), Times.Never);
    }

    [Fact]
    public async Task MedicalTestService_RejectsDocumentOwnedByAnotherPatient()
    {
        await using var db = new PharmacyApiDbContext(
            new DbContextOptionsBuilder<PharmacyApiDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var patient = MakeUser(UserRole.Patient);
        var otherPatient = MakeUser(UserRole.Patient);
        var doctor = MakeUser(UserRole.Doctor);
        var test = new MedicalTest
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            OrderedByDoctorId = doctor.Id,
            OrderedByDoctor = doctor,
            TestType = MedicalTestType.XRay
        };
        var document = new MedicalDocument
        {
            Id = Guid.NewGuid(),
            UserId = otherPatient.Id,
            User = otherPatient,
            UploadedByUserId = otherPatient.Id,
            FileName = "other-patient.pdf",
            ContentType = "application/pdf",
            UploadedAt = DateTime.UtcNow
        };
        db.AddRange(patient, otherPatient, doctor, test, document);
        await db.SaveChangesAsync();
        var service = new MedicalTestService(db, Mock.Of<ILogger<MedicalTestService>>());

        var action = () => service.AttachDocumentAsync(test.Id, document.Id, doctor.Id);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*owner does not match*");
        (await db.MedicalDocuments.FindAsync(document.Id))!.MedicalTestId.Should().BeNull();
    }

    [Fact]
    public async Task MedicalTestService_RejectsSamePatientDocumentNotUploadedByAttachingDoctor()
    {
        await using var db = new PharmacyApiDbContext(
            new DbContextOptionsBuilder<PharmacyApiDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var patient = MakeUser(UserRole.Patient);
        var doctor = MakeUser(UserRole.Doctor);
        var test = new MedicalTest
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            OrderedByDoctorId = doctor.Id,
            OrderedByDoctor = doctor,
            TestType = MedicalTestType.CTScan
        };
        var document = new MedicalDocument
        {
            Id = Guid.NewGuid(),
            UserId = patient.Id,
            User = patient,
            UploadedByUserId = patient.Id,
            FileName = "patient-private.pdf",
            ContentType = "application/pdf",
            UploadedAt = DateTime.UtcNow
        };
        db.AddRange(patient, doctor, test, document);
        await db.SaveChangesAsync();
        var service = new MedicalTestService(db, Mock.Of<ILogger<MedicalTestService>>());

        var action = () => service.AttachDocumentAsync(test.Id, document.Id, doctor.Id);

        await action.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*document uploader*");
        (await db.MedicalDocuments.FindAsync(document.Id))!.MedicalTestId.Should().BeNull();
    }

    private static MedicalTestController MakeMedicalTestController(
        Mock<IMedicalTestService> tests,
        Mock<IReferralService> referrals,
        Mock<IAppointmentService> appointments) =>
        new(tests.Object, referrals.Object, appointments.Object,
            Mock.Of<ILogger<MedicalTestController>>());

    private static ReferralController MakeReferralController(
        Mock<IReferralService> referrals,
        Mock<IPdfExportService> pdf,
        Mock<IAppointmentService> appointments) =>
        new(referrals.Object, pdf.Object, appointments.Object,
            Mock.Of<ILogger<ReferralController>>());

    private static User MakeUser(UserRole role) => new()
    {
        Id = Guid.NewGuid(),
        Username = Guid.NewGuid().ToString("N"),
        PasswordHash = "PRIVATE-HASH",
        FirstName = role.ToString(),
        LastName = "User",
        Email = $"{Guid.NewGuid():N}@example.com",
        PhoneNumber = "+994501234567",
        Role = role,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static void SetUser(ControllerBase controller, Guid userId, string role)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, role)
                ], "Test"))
            }
        };
    }
}
