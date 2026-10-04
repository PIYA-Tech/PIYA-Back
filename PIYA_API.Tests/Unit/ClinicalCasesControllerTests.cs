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
    [Fact]
    public async Task CorrectionPreservesOriginalAndRequiresOwnNoteInSameActiveCase()
    {
        await using var f = await Fixture.Create();
        await f.Controller.Admit(f.Request);
        var c = await f.Db.ClinicalCases.SingleAsync();
        await f.Controller.AddEvent(c.Id, new(1, "Note", "Original observation"));
        var original = await f.Db.ClinicalCaseEvents.SingleAsync(e => e.Kind == "Note");
        Assert.Equal(400, Status(await f.Controller.AddEvent(c.Id, new(2, "Correction", "Correction", Guid.NewGuid()))));
        Assert.Equal(400, Status(await f.Controller.AddEvent(c.Id, new(2, "Correction", " ", original.Id))));
        original.AuthorId = Guid.NewGuid(); await f.Db.SaveChangesAsync();
        Assert.Equal(400, Status(await f.Controller.AddEvent(c.Id, new(2, "Correction", "Correction", original.Id))));
        original.AuthorId = f.Grant.RequesterId; await f.Db.SaveChangesAsync();
        Assert.Equal(200, Status(await f.Controller.AddEvent(c.Id, new(2, "Correction", "Corrected observation and reason", original.Id))));
        Assert.Equal("Original observation", original.Text);
        Assert.Equal(original.Id, (await f.Db.ClinicalCaseEvents.SingleAsync(e => e.Kind == "Correction")).RelatedEventId);
        Assert.Equal(409, Status(await f.Controller.AddEvent(c.Id, new(2, "Correction", "Stale edit", original.Id))));
        await f.Controller.AddEvent(c.Id, new(3, "Discharge", "Complete"));
        Assert.Equal(409, Status(await f.Controller.AddEvent(c.Id, new(4, "Correction", "Closed", original.Id))));
    }

    [Fact]
    public async Task CasePagesKeepOldActiveAdmissionsVisibleAndRejectInvalidQueries()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 105; i++) f.Db.ClinicalCases.Add(new ClinicalCase {
            PatientId = f.Grant.PatientId, AttendingDoctorId = f.Grant.RequesterId,
            HospitalId = f.Request.HospitalId, AdmissionGrantId = Guid.NewGuid(), IsDemo = true,
            Status = i == 0 ? "Active" : "Discharged", AdmittedAt = DateTime.UtcNow.AddDays(i - 106)
        });
        await f.Db.SaveChangesAsync();
        var active = Assert.IsType<OkObjectResult>(await f.Controller.List("Active"));
        Assert.Single(Assert.IsType<List<object>>(active.Value));
        var page = Assert.IsType<OkObjectResult>(await f.Controller.List("Discharged", 2, 100));
        Assert.Equal(4, Assert.IsType<List<object>>(page.Value).Count);
        Assert.Equal(400, Status(await f.Controller.List("unknown")));
        Assert.Equal(400, Status(await f.Controller.List(page: 0)));
        Assert.Equal(400, Status(await f.Controller.List(pageSize: 101)));
        (await f.Db.DoctorProfiles.SingleAsync()).HospitalIds = [];
        await f.Db.SaveChangesAsync();
        Assert.Empty(Assert.IsType<List<object>>(Assert.IsType<OkObjectResult>(await f.Controller.List()).Value));
    }

    [Fact]
    public async Task PatientEpisodesAreOwnOnlyAndDoNotExposeInternalChart()
    {
        await using var f = await Fixture.Create();
        await f.Controller.Admit(f.Request);
        var own = await f.Db.ClinicalCases.SingleAsync();
        await f.Controller.AddEvent(own.Id, new(1, "Note", "INTERNAL SECRET"));
        f.Db.ClinicalCases.Add(new ClinicalCase { PatientId = Guid.NewGuid(), AttendingDoctorId = own.AttendingDoctorId,
            HospitalId = own.HospitalId, AdmissionGrantId = Guid.NewGuid(), AdmissionReason = "OTHER PATIENT" });
        await f.Db.SaveChangesAsync();
        var controller = new PatientCasesController(f.Db, Mock.Of<IAuditService>()) { ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, f.Grant.PatientId.ToString()), new Claim(ClaimTypes.Role, "Patient")], "Test")) }
        } };
        var result = Assert.IsType<OkObjectResult>(await controller.List());
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains(own.Id.ToString(), json);
        Assert.DoesNotContain("INTERNAL SECRET", json); Assert.DoesNotContain("OTHER PATIENT", json);
        Assert.DoesNotContain("AdmissionGrantId", json); Assert.DoesNotContain("Events", json);
        Assert.Equal(400, Status(await controller.List(page: -1)));
        Assert.Equal(400, Status(await controller.List(pageSize: 101)));
        (await f.Db.Users.SingleAsync(u => u.Id == f.Grant.PatientId)).IsActive = false;
        await f.Db.SaveChangesAsync();
        Assert.Equal(403, Status(await controller.List()));
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled, 400)]
    [InlineData(AppointmentStatus.Confirmed, 400)]
    [InlineData(AppointmentStatus.InProgress, 200)]
    [InlineData(AppointmentStatus.Completed, 200)]
    [InlineData(AppointmentStatus.Cancelled, 400)]
    [InlineData(AppointmentStatus.NoShow, 400)]
    [InlineData(AppointmentStatus.Rescheduled, 400)]
    public async Task VisitAdmissionAllowsOnlyStartedOrCompletedConsultations(AppointmentStatus state, int expected)
    {
        await using var f = await Fixture.Create();
        var visit = await AddVisit(f, state);
        // Ordinary care must not depend on an emergency-sharing token.
        f.Db.EmergencyAccessGrants.Remove(f.Grant);
        f.Db.EmergencyHealthProfiles.RemoveRange(f.Db.EmergencyHealthProfiles);
        await f.Db.SaveChangesAsync();
        Assert.Equal(expected, Status(await f.Controller.AdmitFromVisit(new(visit.Id, "Ward", "B1", "Needs ongoing care", true))));
        Assert.Equal(expected == 200 ? 1 : 0, await f.Db.ClinicalCases.CountAsync());
        Assert.Equal(state, visit.Status); // Admission does not silently complete a consultation.
        if (expected == 200) {
            var c = await f.Db.ClinicalCases.SingleAsync();
            Assert.Equal(visit.Id, c.AdmissionAppointmentId); Assert.Null(c.AdmissionGrantId);
            Assert.Equal(visit.PatientId, c.PatientId); Assert.Equal(visit.HospitalId, c.HospitalId);
            Assert.Equal(visit.DoctorId, c.AttendingDoctorId);
            Assert.Contains(visit.Id.ToString(), (await f.Db.ClinicalCaseEvents.SingleAsync()).Text);
            Assert.Equal(200, Status(await f.Controller.AddEvent(c.Id, new(1, "Note", "Assessment documented"))));
            Assert.Equal(200, Status(await f.Controller.AddEvent(c.Id, new(2, "Discharge", "Care completed"))));
            Assert.Equal(409, Status(await f.Controller.AdmitFromVisit(new(visit.Id, "Ward", "", "Retry", true))));
        }
    }

    [Theory]
    [InlineData("another-doctor", 404)]
    [InlineData("another-patient-demo", 404)]
    [InlineData("missing-visit", 404)]
    [InlineData("no-hospital-assignment", 403)]
    [InlineData("inactive-hospital", 403)]
    [InlineData("inactive-doctor", 403)]
    [InlineData("inactive-patient", 403)]
    [InlineData("future", 400)]
    [InlineData("no-confirmation", 400)]
    [InlineData("blank-reason", 400)]
    [InlineData("blank-department", 400)]
    public async Task VisitAdmissionRejectsInvalidAuthorityAndContext(string scenario, int expected)
    {
        await using var f = await Fixture.Create();
        var visit = await AddVisit(f, AppointmentStatus.InProgress);
        if (scenario == "another-doctor") visit.DoctorId = Guid.NewGuid();
        if (scenario == "another-patient-demo") visit.PatientId = Guid.NewGuid();
        if (scenario == "no-hospital-assignment") (await f.Db.DoctorProfiles.SingleAsync()).HospitalIds = [];
        if (scenario == "inactive-hospital") (await f.Db.Hospitals.SingleAsync()).IsActive = false;
        if (scenario == "inactive-doctor") (await f.Db.Users.SingleAsync(u => u.Id == visit.DoctorId)).IsActive = false;
        if (scenario == "inactive-patient") (await f.Db.Users.SingleAsync(u => u.Id == visit.PatientId)).IsActive = false;
        if (scenario == "future") visit.ScheduledAt = DateTime.UtcNow.AddDays(1);
        await f.Db.SaveChangesAsync();
        var result = await f.Controller.AdmitFromVisit(new(scenario == "missing-visit" ? Guid.NewGuid() : visit.Id,
            scenario == "blank-department" ? " " : "Ward", "", scenario == "blank-reason" ? " " : "Care", scenario != "no-confirmation"));
        Assert.Equal(expected, Status(result)); Assert.Empty(f.Db.ClinicalCases); Assert.Empty(f.Db.ClinicalCaseEvents);
    }

    [Fact]
    public async Task VisitAdmissionRequiresRealDoctorsHospitalPrivilege()
    {
        await using var f = await Fixture.Create(demo: false);
        var visit = await AddVisit(f, AppointmentStatus.Completed);
        var request = new AdmitVisitRequest(visit.Id, "Ward", null, "Care", true);
        Assert.Equal(403, Status(await f.Controller.AdmitFromVisit(request)));
        var permission = new UserPermission { UserId = visit.DoctorId, Permission = Permissions.ClinicalCaseAdmit, ResourceId = visit.HospitalId.ToString(), ExpiresAt = DateTime.UtcNow.AddMinutes(-1) };
        f.Db.UserPermissions.Add(permission); await f.Db.SaveChangesAsync();
        Assert.Equal(403, Status(await f.Controller.AdmitFromVisit(request)));
        permission.ExpiresAt = DateTime.UtcNow.AddHours(1); await f.Db.SaveChangesAsync();
        Assert.Equal(200, Status(await f.Controller.AdmitFromVisit(request)));
        Assert.False((await f.Db.ClinicalCases.SingleAsync()).IsDemo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CannotDuplicateActiveCaseAcrossAdmissionRoutes(bool qrFirst)
    {
        await using var f = await Fixture.Create();
        var visit = await AddVisit(f, AppointmentStatus.InProgress);
        var request = new AdmitVisitRequest(visit.Id, "Ward", "", "Care", true);
        if (qrFirst) {
            Assert.Equal(200, Status(await f.Controller.Admit(f.Request)));
            Assert.Equal(409, Status(await f.Controller.AdmitFromVisit(request)));
        } else {
            Assert.Equal(200, Status(await f.Controller.AdmitFromVisit(request)));
            Assert.Equal(409, Status(await f.Controller.Admit(f.Request)));
        }
        Assert.Single(f.Db.ClinicalCases);
    }

    private static async Task<Appointment> AddVisit(Fixture f, AppointmentStatus status)
    {
        var visit = new Appointment { Id = Guid.NewGuid(), PatientId = f.Grant.PatientId, DoctorId = f.Grant.RequesterId,
            HospitalId = f.Request.HospitalId, ScheduledAt = DateTime.UtcNow.AddHours(-1), Status = status };
        f.Db.Appointments.Add(visit); await f.Db.SaveChangesAsync(); return visit;
    }

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
