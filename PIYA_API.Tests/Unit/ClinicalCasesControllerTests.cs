using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using PIYA_API.Controllers;
using PIYA_API.Data;
using PIYA_API.Middleware;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class ClinicalCasesControllerTests
{
    [Theory]
    [InlineData("valid", 200)]
    [InlineData("expired", 400)]
    [InlineData("revoked", 400)]
    [InlineData("wrong-doctor", 400)]
    [InlineData("wrong-patient", 400)]
    [InlineData("no-assignment", 403)]
    [InlineData("no-confirmation", 400)]
    [InlineData("sharing-disabled", 400)]
    public async Task AdmissionRequiresExplicitResponsibilityAndScopedGrant(string scenario, int status)
    {
        await using var f = await Fixture.Create();
        if (scenario == "expired") f.Grant.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        if (scenario == "revoked") f.Grant.RevokedAt = DateTime.UtcNow;
        if (scenario == "wrong-doctor") f.Grant.RequesterId = Guid.NewGuid();
        if (scenario == "wrong-patient") f.Grant.PatientId = Guid.NewGuid();
        if (scenario == "no-assignment") (await f.Db.DoctorProfiles.SingleAsync()).HospitalIds = [];
        if (scenario == "sharing-disabled") (await f.Db.EmergencyHealthProfiles.SingleAsync()).IsSharingEnabled = false;
        await f.Db.SaveChangesAsync();
        var result = await f.Controller.Admit(f.Request with { AcceptResponsibility = scenario != "no-confirmation" });
        Assert.Equal(status, Status(result));
        Assert.Equal(status == 200 ? 1 : 0, await f.Db.ClinicalCases.CountAsync());
        if (status == 200) {
            Assert.Equal("Admission", (await f.Db.ClinicalCaseEvents.SingleAsync()).Kind);
            Assert.Equal(409, Status(await f.Controller.Admit(f.Request)));
        }
    }

    [Fact]
    public async Task CaseLifecycleRequiresAlertAcknowledgementAndPreservesHistory()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(200, Status(await f.Controller.Admit(f.Request)));
        var c = await f.Db.ClinicalCases.SingleAsync();
        Assert.Equal(200, Status(await f.Controller.AddEvent(c.Id, new(1, "DemoAlert", ""))));
        var alert = await f.Db.ClinicalCaseEvents.SingleAsync(e => e.Kind == "DemoAlert");
        Assert.Equal(400, Status(await f.Controller.AddEvent(c.Id, new(2, "Resolve", "Response", alert.Id))));
        Assert.Equal(409, Status(await f.Controller.AddEvent(c.Id, new(2, "Discharge", "Outcome"))));
        Assert.Equal(200, Status(await f.Controller.AddEvent(c.Id, new(2, "Acknowledge", "", alert.Id))));
        Assert.Equal(200, Status(await f.Controller.AddEvent(c.Id, new(3, "Resolve", "Demo response documented", alert.Id))));
        Assert.Equal(200, Status(await f.Controller.AddEvent(c.Id, new(4, "Note", "Fictional chart note"))));
        Assert.Equal(409, Status(await f.Controller.AddEvent(c.Id, new(4, "Note", "Stale retry"))));
        Assert.Equal(200, Status(await f.Controller.AddEvent(c.Id, new(5, "Discharge", "Demo discharge summary"))));
        Assert.Equal(409, Status(await f.Controller.AddEvent(c.Id, new(6, "Note", "After discharge"))));
        Assert.Equal(6, await f.Db.ClinicalCaseEvents.CountAsync());
        Assert.Equal("Discharged", c.Status);
    }

    [Fact]
    public async Task OtherDoctorAndRevokedHospitalAssignmentCannotReadCase()
    {
        await using var f = await Fixture.Create();
        await f.Controller.Admit(f.Request);
        var c = await f.Db.ClinicalCases.SingleAsync();
        Assert.Equal(200, Status(await f.Controller.Get(c.Id)));
        c.AttendingDoctorId = Guid.NewGuid(); await f.Db.SaveChangesAsync();
        Assert.Equal(404, Status(await f.Controller.Get(c.Id)));
        c.AttendingDoctorId = ConferenceDoctorScope.DoctorId;
        (await f.Db.DoctorProfiles.SingleAsync()).HospitalIds = [];
        await f.Db.SaveChangesAsync();
        Assert.Equal(404, Status(await f.Controller.Get(c.Id)));
    }

    [Fact]
    public async Task RealDoctorNeedsHospitalScopedPermissionAndCannotInjectDemoVitals()
    {
        await using var f = await Fixture.Create(demo: false);
        Assert.Equal(403, Status(await f.Controller.Admit(f.Request)));
        var permission = new UserPermission { Id = Guid.NewGuid(), UserId = f.Grant.RequesterId,
            Permission = "ClinicalCase.Admit", ResourceId = Guid.NewGuid().ToString() };
        f.Db.UserPermissions.Add(permission); await f.Db.SaveChangesAsync();
        Assert.Equal(403, Status(await f.Controller.Admit(f.Request)));
        permission.ResourceId = f.Request.HospitalId.ToString(); await f.Db.SaveChangesAsync();
        Assert.Equal(200, Status(await f.Controller.Admit(f.Request)));
        var c = await f.Db.ClinicalCases.SingleAsync();
        Assert.False(c.IsDemo);
        Assert.Equal(403, Status(await f.Controller.AddEvent(c.Id, new(1, "DemoReading", ""))));
        permission.IsActive = false; await f.Db.SaveChangesAsync();
        Assert.Equal(404, Status(await f.Controller.Get(c.Id)));
    }

    private static int Status(IActionResult result) => result is ForbidResult ? 403 : result is StatusCodeResult code ? code.StatusCode : ((ObjectResult)result).StatusCode ?? 200;
    private sealed class Fixture : IAsyncDisposable
    {
        public required PharmacyApiDbContext Db { get; init; }
        public required ClinicalCasesController Controller { get; init; }
        public required EmergencyAccessGrant Grant { get; init; }
        public required AdmitCaseRequest Request { get; init; }
        public static async Task<Fixture> Create(bool demo = true)
        {
            var db = new PharmacyApiDbContext(new DbContextOptionsBuilder<PharmacyApiDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var doctor = User(demo ? ConferenceDoctorScope.DoctorId : Guid.NewGuid(), UserRole.Doctor);
            var patient = User(ConferenceDoctorScope.PatientId, UserRole.Patient);
            patient.Username = "conference_patient"; patient.Email = "conference.patient@example.invalid";
            var hospital = new Hospital { Id = Guid.NewGuid(), Name = "Fictional hospital", Address = "Demo", City = "Baku", Country = "AZ", PhoneNumber = "000", IsActive = true };
            db.Users.AddRange(doctor, patient); db.Hospitals.Add(hospital);
            db.DoctorProfiles.Add(new DoctorProfile { Id = Guid.NewGuid(), UserId = doctor.Id, LicenseNumber = "Test-only", HospitalIds = [hospital.Id] });
            db.EmergencyHealthProfiles.Add(new EmergencyHealthProfile { Id = Guid.NewGuid(), PatientId = patient.Id, IsSharingEnabled = true, ShareTokenExpiresAt = DateTime.UtcNow.AddHours(1) });
            var grant = new EmergencyAccessGrant { Id = Guid.NewGuid(), PatientId = patient.Id, RequesterId = doctor.Id, Reason = "Test", ExpiresAt = DateTime.UtcNow.AddMinutes(20) };
            db.EmergencyAccessGrants.Add(grant); await db.SaveChangesAsync();
            var controller = new ClinicalCasesController(db, Mock.Of<IAuditService>()) { ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, doctor.Id.ToString()), new Claim(ClaimTypes.Role, "Doctor")], "Test")) }
            } };
            return new Fixture { Db = db, Controller = controller, Grant = grant, Request = new(grant.Id, hospital.Id, "ER", "Demo bed", "Conference scenario", true) };
        }
        private static User User(Guid id, UserRole role) => new() { Id = id, Username = id.ToString(), Email = id + "@example.invalid", PasswordHash = "unused", FirstName = "Fictional", LastName = "Test", PhoneNumber = "000", Role = role, IsActive = true };
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
