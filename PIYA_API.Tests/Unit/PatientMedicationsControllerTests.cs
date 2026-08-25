using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Controllers;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class PatientMedicationsControllerTests
{
    [Fact]
    public async Task Patient_CanRequestOwnActivePrescription_FromStaffedPharmacy()
    {
        await using var db = CreateContext();
        var scenario = await SeedScenario(db);
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.GetPharmacyStaffAsync(scenario.Pharmacy.Id, true))
            .ReturnsAsync([scenario.Assignment]);
        var audit = new Mock<IAuditService>();
        var notifications = new Mock<INotificationService>();
        var controller = CreateController(db, staff, audit, notifications, scenario.Patient.Id, "Patient");

        var result = await controller.CreateRefillRequest(new CreatePatientRefillRequest(
            scenario.Prescription.Id, scenario.Item.Id, scenario.Pharmacy.Id, true));

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = created.Value.Should().BeOfType<PatientRefillResponse>().Subject;
        response.Status.Should().Be(PatientRefillRequestStatus.Pending);
        response.MedicationName.Should().Be("DemoMed");
        response.PharmacyName.Should().Be("PIYA Pharmacy");
        response.AutoRefill.Should().BeTrue();
        (await db.PatientRefillRequests.SingleAsync()).PatientId.Should().Be(scenario.Patient.Id);
        audit.Verify(service => service.LogEntityActionAsync(
            "CreatePatientRefillRequest", nameof(PatientRefillRequest), It.IsAny<string>(),
            scenario.Patient.Id, It.IsAny<string>()), Times.Once);
        notifications.Verify(service => service.SendPushNotificationAsync(
            scenario.Pharmacist.Id, "New refill request", It.IsAny<string>(),
            It.Is<Dictionary<string, string>>(data =>
                data["type"] == "patient_refill_request" && !data.ContainsKey("medication"))), Times.Once);
    }

    [Fact]
    public async Task Patient_CannotRequestAnotherPatientsPrescription()
    {
        await using var db = CreateContext();
        var scenario = await SeedScenario(db);
        var stranger = MakeUser(UserRole.Patient);
        db.Users.Add(stranger);
        await db.SaveChangesAsync();
        var controller = CreateController(
            db, new Mock<IPharmacyStaffService>(), new Mock<IAuditService>(),
            new Mock<INotificationService>(), stranger.Id, "Patient");

        var result = await controller.CreateRefillRequest(new CreatePatientRefillRequest(
            scenario.Prescription.Id, scenario.Item.Id, scenario.Pharmacy.Id));

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        (await db.PatientRefillRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PharmacyStaff_CannotSkipControlledStatusTransitions()
    {
        await using var db = CreateContext();
        var scenario = await SeedScenario(db);
        var refill = new PatientRefillRequest
        {
            Id = Guid.NewGuid(), PatientId = scenario.Patient.Id,
            PrescriptionId = scenario.Prescription.Id, PrescriptionItemId = scenario.Item.Id,
            PharmacyId = scenario.Pharmacy.Id, Status = PatientRefillRequestStatus.Pending
        };
        db.PatientRefillRequests.Add(refill);
        await db.SaveChangesAsync();
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.IsStaffAtPharmacyAsync(scenario.Pharmacy.Id, scenario.Pharmacist.Id))
            .ReturnsAsync(true);
        var controller = CreateController(
            db, staff, new Mock<IAuditService>(), new Mock<INotificationService>(),
            scenario.Pharmacist.Id, "Pharmacist");

        var result = await controller.UpdateRefillRequestStatus(
            refill.Id, new UpdatePatientRefillStatusRequest(PatientRefillRequestStatus.Ready));

        result.Result.Should().BeOfType<ConflictObjectResult>();
        (await db.PatientRefillRequests.FindAsync(refill.Id))!.Status
            .Should().Be(PatientRefillRequestStatus.Pending);
    }

    private static PatientMedicationsController CreateController(
        PharmacyApiDbContext db,
        Mock<IPharmacyStaffService> staff,
        Mock<IAuditService> audit,
        Mock<INotificationService> notifications,
        Guid userId,
        string role)
    {
        var controller = new PatientMedicationsController(
            db, staff.Object, audit.Object, notifications.Object,
            Mock.Of<ILogger<PatientMedicationsController>>());
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
        return controller;
    }

    private static PharmacyApiDbContext CreateContext() => new(
        new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Scenario> SeedScenario(PharmacyApiDbContext db)
    {
        var patient = MakeUser(UserRole.Patient);
        var doctor = MakeUser(UserRole.Doctor);
        var pharmacist = MakeUser(UserRole.Pharmacist);
        var company = new PharmacyCompany { Id = Guid.NewGuid(), Name = "PIYA Network" };
        var pharmacy = new Pharmacy
        {
            Id = Guid.NewGuid(), Country = "Azerbaijan", City = "Baku",
            Name = "PIYA Pharmacy", Address = "Baku",
            Coordinates = new Coordinates { Id = Guid.NewGuid(), Latitude = 40.4, Longitude = 49.8 },
            Company = company, IsActive = true
        };
        var medication = new Medication
        {
            Id = Guid.NewGuid(), BrandName = "DemoMed", GenericName = "Demo",
            Form = "Tablet", Strength = "10 mg"
        };
        var prescription = new Prescription
        {
            Id = Guid.NewGuid(), PatientId = patient.Id, Patient = patient,
            DoctorId = doctor.Id, Doctor = doctor, Status = PrescriptionStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        var item = new PrescriptionItem
        {
            Id = Guid.NewGuid(), PrescriptionId = prescription.Id, Prescription = prescription,
            MedicationId = medication.Id, Medication = medication,
            Dosage = "10 mg", Frequency = "Once daily", Duration = "30 days", Quantity = 30
        };
        prescription.Items.Add(item);
        var assignment = new PharmacyStaff
        {
            Id = Guid.NewGuid(), PharmacyId = pharmacy.Id, Pharmacy = pharmacy,
            UserId = pharmacist.Id, User = pharmacist, Role = PharmacyStaffRole.Staff,
            IsActive = true
        };
        db.AddRange(patient, doctor, pharmacist, company, pharmacy, medication, prescription, assignment);
        await db.SaveChangesAsync();
        return new Scenario(patient, pharmacist, pharmacy, prescription, item, assignment);
    }

    private static User MakeUser(UserRole role) => new()
    {
        Id = Guid.NewGuid(), Username = Guid.NewGuid().ToString("N"), PasswordHash = "hash",
        FirstName = role.ToString(), LastName = "User", Email = $"{Guid.NewGuid():N}@example.com",
        PhoneNumber = "+994501234567", Role = role, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private sealed record Scenario(
        User Patient,
        User Pharmacist,
        Pharmacy Pharmacy,
        Prescription Prescription,
        PrescriptionItem Item,
        PharmacyStaff Assignment);
}
