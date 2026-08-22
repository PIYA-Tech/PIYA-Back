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

public class QrAuthorizationControllerTests
{
    [Fact]
    public async Task RevokeQr_UnrelatedPatient_IsForbiddenBeforeMutation()
    {
        var callerId = Guid.NewGuid();
        var prescriptionId = Guid.NewGuid();
        var qr = new Mock<IQRService>();
        qr.Setup(service => service.ValidateQrTokenAsync("token"))
            .ReturnsAsync((true, prescriptionId, "Prescription", DateTime.UtcNow.AddMinutes(5), ""));
        var prescriptions = new Mock<IPrescriptionService>();
        prescriptions.Setup(service => service.GetByIdAsync(prescriptionId))
            .ReturnsAsync(MakePrescription(prescriptionId, Guid.NewGuid(), Guid.NewGuid()));
        var controller = CreateQrController(qr, prescriptions);
        SetUser(controller, callerId, "Patient");

        var result = await controller.RevokeQR(new RevokeQRRequest
        {
            Token = "token",
            Reason = "No longer needed"
        });

        result.Result.Should().BeOfType<ForbidResult>();
        qr.Verify(
            service => service.RevokeTokenAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task RevokeQr_OwningPatient_CanRevoke()
    {
        var patientId = Guid.NewGuid();
        var prescriptionId = Guid.NewGuid();
        var qr = new Mock<IQRService>();
        qr.Setup(service => service.ValidateQrTokenAsync("token"))
            .ReturnsAsync((true, prescriptionId, "Prescription", DateTime.UtcNow.AddMinutes(5), ""));
        qr.Setup(service => service.RevokeTokenAsync("token", patientId, "No longer needed"))
            .ReturnsAsync(true);
        var prescriptions = new Mock<IPrescriptionService>();
        prescriptions.Setup(service => service.GetByIdAsync(prescriptionId))
            .ReturnsAsync(MakePrescription(prescriptionId, patientId, Guid.NewGuid()));
        var controller = CreateQrController(qr, prescriptions);
        SetUser(controller, patientId, "Patient");

        var result = await controller.RevokeQR(new RevokeQRRequest
        {
            Token = "token",
            Reason = "No longer needed"
        });

        result.Result.Should().BeOfType<OkObjectResult>();
        qr.Verify(
            service => service.RevokeTokenAsync("token", patientId, "No longer needed"),
            Times.Once);
    }

    [Fact]
    public async Task QrValidationScan_UsesAtomicDispensingServiceMethod()
    {
        var pharmacistId = Guid.NewGuid();
        var pharmacyId = Guid.NewGuid();
        var prescriptionId = Guid.NewGuid();
        var qr = new Mock<IQRService>();
        var prescriptions = new Mock<IPrescriptionService>();
        prescriptions.Setup(service => service.FulfillPrescriptionByQrAsync(
                "token",
                pharmacistId,
                pharmacyId,
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(MakePrescription(
                prescriptionId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                PrescriptionStatus.Fulfilled,
                pharmacyId));
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.GetUserPharmaciesAsync(pharmacistId, true))
            .ReturnsAsync(
            [
                new PharmacyStaff
                {
                    PharmacyId = pharmacyId,
                    UserId = pharmacistId,
                    IsActive = true
                }
            ]);
        var controller = CreateQrController(qr, prescriptions, staff);
        SetUser(controller, pharmacistId, "Pharmacist");

        var result = await controller.ScanPrescriptionQR(new ScanQRRequest { QrToken = "token" });

        result.Result.Should().BeOfType<OkObjectResult>();
        prescriptions.Verify(
            service => service.FulfillPrescriptionByQrAsync(
                "token",
                pharmacistId,
                pharmacyId,
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Once);
        qr.Verify(
            service => service.MarkTokenAsUsedAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task PrescriptionQrValidation_UsesAtomicDispensingServiceMethod()
    {
        var pharmacistId = Guid.NewGuid();
        var pharmacyId = Guid.NewGuid();
        var prescriptionId = Guid.NewGuid();
        var qr = new Mock<IQRService>();
        var prescriptions = new Mock<IPrescriptionService>();
        prescriptions.Setup(service => service.FulfillPrescriptionByQrAsync(
                "token",
                pharmacistId,
                pharmacyId,
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(MakePrescription(
                prescriptionId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                PrescriptionStatus.Fulfilled,
                pharmacyId));
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.GetUserPharmaciesAsync(pharmacistId, true))
            .ReturnsAsync(
            [
                new PharmacyStaff
                {
                    PharmacyId = pharmacyId,
                    UserId = pharmacistId,
                    IsActive = true
                }
            ]);
        var controller = new PrescriptionController(
            prescriptions.Object,
            qr.Object,
            staff.Object,
            Mock.Of<IAppointmentService>(),
            Options.Create(new SecurityOptions()),
            Mock.Of<ILogger<PrescriptionController>>());
        SetUser(controller, pharmacistId, "Pharmacist");

        var result = await controller.ValidateQrCode(new ValidateQrRequest("token"));

        result.Result.Should().BeOfType<OkObjectResult>();
        prescriptions.Verify(
            service => service.FulfillPrescriptionByQrAsync(
                "token",
                pharmacistId,
                pharmacyId,
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Once);
        qr.Verify(
            service => service.MarkTokenAsUsedAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task PrescriptionPreview_DoesNotConsumeTokenOrMutatePrescription()
    {
        var pharmacistId = Guid.NewGuid();
        var pharmacyId = Guid.NewGuid();
        var medicationId = Guid.NewGuid();
        var prescriptionId = Guid.NewGuid();
        var qr = new Mock<IQRService>();
        qr.Setup(service => service.ValidateQrTokenAsync("token"))
            .ReturnsAsync((true, prescriptionId, "Prescription", DateTime.UtcNow.AddMinutes(5), ""));
        var prescription = MakePrescription(prescriptionId, Guid.NewGuid(), Guid.NewGuid());
        prescription.Patient = new User
        {
            Id = prescription.PatientId, Username = "patient", PasswordHash = "hash",
            FirstName = "Demo", LastName = "Patient", Email = "patient@example.com",
            PhoneNumber = "+994501234567"
        };
        prescription.Items.Add(new PrescriptionItem
        {
            Id = Guid.NewGuid(), PrescriptionId = prescriptionId, MedicationId = medicationId,
            Medication = new Medication
            {
                Id = medicationId, BrandName = "DemoMed", GenericName = "Demo Generic",
                Form = "Tablet", Strength = "10mg"
            },
            Dosage = "10mg", Frequency = "Daily", Duration = "7 days", Quantity = 2
        });
        var prescriptions = new Mock<IPrescriptionService>();
        prescriptions.Setup(service => service.GetByIdAsync(prescriptionId)).ReturnsAsync(prescription);
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(service => service.GetAvailableStockAsync(pharmacyId, medicationId)).ReturnsAsync(5);
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.GetUserPharmaciesAsync(pharmacistId, true)).ReturnsAsync(
        [
            new PharmacyStaff { PharmacyId = pharmacyId, UserId = pharmacistId, IsActive = true }
        ]);
        var controller = CreateQrController(qr, prescriptions, staff, inventory);
        SetUser(controller, pharmacistId, "Pharmacist");

        var result = await controller.PreviewPrescriptionQR(new ScanQRRequest { QrToken = "token" });

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<PrescriptionPreviewResponse>().Which.CanFulfill.Should().BeTrue();
        prescriptions.Verify(service => service.FulfillPrescriptionByQrAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
        qr.Verify(service => service.MarkTokenAsUsedAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    private static QRValidationController CreateQrController(
        Mock<IQRService> qr,
        Mock<IPrescriptionService> prescriptions,
        Mock<IPharmacyStaffService>? staff = null,
        Mock<IInventoryService>? inventory = null) =>
        new(
            qr.Object,
            prescriptions.Object,
            inventory?.Object ?? Mock.Of<IInventoryService>(),
            staff?.Object ?? Mock.Of<IPharmacyStaffService>(),
            Options.Create(new SecurityOptions()),
            Mock.Of<ILogger<QRValidationController>>());

    private static Prescription MakePrescription(
        Guid id,
        Guid patientId,
        Guid doctorId,
        PrescriptionStatus status = PrescriptionStatus.Active,
        Guid? pharmacyId = null) =>
        new()
        {
            Id = id,
            PatientId = patientId,
            DoctorId = doctorId,
            Status = status,
            FulfilledByPharmacyId = pharmacyId,
            ExpiresAt = DateTime.UtcNow.AddDays(5)
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
                ],
                "Test"))
            }
        };
    }
}
