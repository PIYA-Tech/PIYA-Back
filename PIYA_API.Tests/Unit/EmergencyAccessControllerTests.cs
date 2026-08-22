using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

public class EmergencyAccessControllerTests
{
    [Fact]
    public async Task LicensedDoctor_WithPatientToken_GetsAuditedTimeLimitedGrant()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient);
        var doctor = MakeUser(UserRole.Doctor);
        const string token = "patient-controlled-token";
        db.Users.AddRange(patient, doctor);
        db.DoctorProfiles.Add(new DoctorProfile
        {
            Id = Guid.NewGuid(), UserId = doctor.Id, User = doctor,
            LicenseNumber = "MED-VALID", LicenseExpiryDate = DateTime.UtcNow.AddYears(1),
            Specialization = MedicalSpecialization.GeneralPractice
        });
        db.EmergencyHealthProfiles.Add(new EmergencyHealthProfile
        {
            Id = Guid.NewGuid(), PatientId = patient.Id, Patient = patient,
            Allergies = "Penicillin", IsSharingEnabled = true,
            ShareTokenHash = Hash(token), ShareTokenExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();

        var audit = new Mock<IAuditService>();
        var fcm = new Mock<IFcmService>();
        var controller = CreateController(db, audit, fcm, doctor.Id, "Doctor");

        var result = await controller.RequestAccess(
            new EmergencyAccessRequest(token, "Patient is unresponsive", "Demo ER"));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<EmergencyRecordResponse>().Subject;
        response.Patient.Id.Should().Be(patient.Id);
        response.EmergencyProfile.Allergies.Should().Be("Penicillin");
        response.AccessExpiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(29));
        (await db.EmergencyAccessGrants.CountAsync()).Should().Be(1);
        audit.Verify(service => service.LogAsync(It.Is<AuditLog>(log =>
            log.Action == "EmergencyRecordAccess" && log.IsSuccess)), Times.Once);
        fcm.Verify(service => service.SendToUserAsync(
            patient.Id, It.IsAny<string>(), It.IsAny<string>(),
            It.Is<Dictionary<string, string>>(data => data["type"] == "emergencyAccess")), Times.Once);
    }

    [Fact]
    public async Task DoctorWithoutCurrentLicense_CannotOpenEmergencyRecord()
    {
        await using var db = CreateContext();
        var doctor = MakeUser(UserRole.Doctor);
        db.Users.Add(doctor);
        db.DoctorProfiles.Add(new DoctorProfile
        {
            Id = Guid.NewGuid(), UserId = doctor.Id, User = doctor,
            LicenseNumber = "EXPIRED", LicenseExpiryDate = DateTime.UtcNow.AddDays(-1),
            Specialization = MedicalSpecialization.GeneralPractice
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, new Mock<IAuditService>(), new Mock<IFcmService>(), doctor.Id, "Doctor");

        var result = await controller.RequestAccess(
            new EmergencyAccessRequest("any-token", "Emergency care", "Demo ER"));

        var forbidden = result.Result.Should().BeOfType<ObjectResult>().Subject;
        forbidden.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await db.EmergencyAccessGrants.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InvalidToken_IsRejectedAndAuditedWithoutGrant()
    {
        await using var db = CreateContext();
        var doctor = MakeUser(UserRole.Doctor);
        db.Users.Add(doctor);
        db.DoctorProfiles.Add(new DoctorProfile
        {
            Id = Guid.NewGuid(), UserId = doctor.Id, User = doctor,
            LicenseNumber = "VALID", LicenseExpiryDate = DateTime.UtcNow.AddDays(1),
            Specialization = MedicalSpecialization.GeneralPractice
        });
        await db.SaveChangesAsync();
        var audit = new Mock<IAuditService>();
        var controller = CreateController(db, audit, new Mock<IFcmService>(), doctor.Id, "Doctor");

        var result = await controller.RequestAccess(
            new EmergencyAccessRequest("wrong-token", "Emergency care", "Demo ER"));

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        (await db.EmergencyAccessGrants.CountAsync()).Should().Be(0);
        audit.Verify(service => service.LogAsync(It.Is<AuditLog>(log => !log.IsSuccess)), Times.Once);
    }

    [Fact]
    public async Task MissingFacility_IsRejectedBeforeAnyRecordLookup()
    {
        await using var db = CreateContext();
        var doctor = MakeUser(UserRole.Doctor);
        db.Users.Add(doctor);
        await db.SaveChangesAsync();
        var controller = CreateController(
            db, new Mock<IAuditService>(), new Mock<IFcmService>(), doctor.Id, "Doctor");

        var result = await controller.RequestAccess(
            new EmergencyAccessRequest("token", "Emergency care", ""));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await db.EmergencyAccessGrants.CountAsync()).Should().Be(0);
    }

    private static EmergencyAccessController CreateController(
        PharmacyApiDbContext db, Mock<IAuditService> audit, Mock<IFcmService> fcm,
        Guid userId, string role)
    {
        var controller = new EmergencyAccessController(
            db, audit.Object, fcm.Object, Mock.Of<ILogger<EmergencyAccessController>>());
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

    private static User MakeUser(UserRole role) => new()
    {
        Id = Guid.NewGuid(), Username = Guid.NewGuid().ToString("N"), PasswordHash = "hash",
        FirstName = role.ToString(), LastName = "User", Email = $"{Guid.NewGuid():N}@example.com",
        PhoneNumber = "+994501234567", Role = role, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
