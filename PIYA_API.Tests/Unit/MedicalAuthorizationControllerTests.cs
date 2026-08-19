using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PIYA_API.Configuration;
using PIYA_API.Controllers;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class MedicalAuthorizationControllerTests
{
    [Fact]
    public async Task PresignedUrl_UnrelatedDoctor_IsForbidden()
    {
        var doctorId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var files = new Mock<IFileUploadService>();
        var appointments = new Mock<IAppointmentService>();
        files.Setup(x => x.GetDocumentByIdAsync(documentId))
            .ReturnsAsync(MakeDocument(documentId, patientId));
        appointments
            .Setup(x => x.HasDoctorPatientRelationshipAsync(doctorId, patientId))
            .ReturnsAsync(false);

        var controller = MakeFileController(files, appointments);
        SetUser(controller, doctorId, "Doctor");

        var result = await controller.GetPresignedUrl(documentId);

        result.Should().BeOfType<ForbidResult>();
        files.Verify(x => x.GetPresignedUrlAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UploadForPatient_UnrelatedDoctor_IsForbiddenBeforeStorage()
    {
        var doctorId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var files = new Mock<IFileUploadService>();
        var appointments = new Mock<IAppointmentService>();
        appointments
            .Setup(x => x.HasDoctorPatientRelationshipAsync(doctorId, patientId))
            .ReturnsAsync(false);
        var controller = MakeFileController(files, appointments);
        SetUser(controller, doctorId, "Doctor");

        var content = new MemoryStream([1, 2, 3]);
        var request = new UploadDocumentRequest
        {
            UserId = patientId,
            DocumentType = nameof(MedicalDocumentType.LabReport),
            File = new FormFile(content, 0, content.Length, "file", "report.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf"
            }
        };

        var result = await controller.UploadDocument(request);

        result.Should().BeOfType<ForbidResult>();
        files.Verify(x => x.UploadDocumentAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid>(), It.IsAny<MedicalDocumentType>(), It.IsAny<Guid>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task GetPrescription_ActiveButUnassigned_PharmacistIsForbidden()
    {
        var pharmacistId = Guid.NewGuid();
        var prescriptionId = Guid.NewGuid();
        var prescriptions = new Mock<IPrescriptionService>();
        prescriptions.Setup(x => x.GetByIdAsync(prescriptionId))
            .ReturnsAsync(new Prescription
            {
                Id = prescriptionId,
                PatientId = Guid.NewGuid(),
                DoctorId = Guid.NewGuid(),
                Status = PrescriptionStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddDays(5)
            });

        var staff = new Mock<IPharmacyStaffService>();
        var controller = MakePrescriptionController(prescriptions, staff);
        SetUser(controller, pharmacistId, "Pharmacist");

        var result = await controller.GetById(prescriptionId);

        result.Result.Should().BeOfType<ForbidResult>();
        staff.Verify(x => x.IsStaffAtPharmacyAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task DirectInitialFulfillment_RequiresPatientPresentation()
    {
        var pharmacistId = Guid.NewGuid();
        var pharmacyId = Guid.NewGuid();
        var prescriptionId = Guid.NewGuid();
        var prescriptions = new Mock<IPrescriptionService>();
        prescriptions.Setup(x => x.GetByIdAsync(prescriptionId))
            .ReturnsAsync(new Prescription
            {
                Id = prescriptionId,
                PatientId = Guid.NewGuid(),
                DoctorId = Guid.NewGuid(),
                Status = PrescriptionStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddDays(5)
            });
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(x => x.IsStaffAtPharmacyAsync(pharmacyId, pharmacistId)).ReturnsAsync(true);
        var controller = MakePrescriptionController(prescriptions, staff);
        SetUser(controller, pharmacistId, "Pharmacist");

        var result = await controller.FulfillPrescription(
            prescriptionId, new FulfillPrescriptionRequest(pharmacyId));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        prescriptions.Verify(
            x => x.FulfillPrescriptionAsync(It.IsAny<Guid>(), It.IsAny<Guid>()),
            Times.Never);
    }

    private static FileUploadController MakeFileController(
        Mock<IFileUploadService> files,
        Mock<IAppointmentService> appointments) =>
        new(
            files.Object,
            Mock.Of<IFileStorageService>(),
            appointments.Object,
            Mock.Of<IPrescriptionService>(),
            Mock.Of<ILogger<FileUploadController>>());

    private static PrescriptionController MakePrescriptionController(
        Mock<IPrescriptionService> prescriptions,
        Mock<IPharmacyStaffService> staff) =>
        new(
            prescriptions.Object,
            Mock.Of<IQRService>(),
            staff.Object,
            Mock.Of<IAppointmentService>(),
            Options.Create(new SecurityOptions()),
            Mock.Of<ILogger<PrescriptionController>>());

    private static MedicalDocument MakeDocument(Guid id, Guid patientId) => new()
    {
        Id = id,
        UserId = patientId,
        UploadedByUserId = patientId,
        FileName = "report.pdf",
        ContentType = "application/pdf",
        UploadedAt = DateTime.UtcNow
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
