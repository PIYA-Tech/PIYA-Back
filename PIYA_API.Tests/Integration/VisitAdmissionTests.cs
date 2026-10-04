using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Integration;

public class VisitAdmissionTests(PiyaWebApplicationFactory factory) : IClassFixture<PiyaWebApplicationFactory>
{
    [Fact]
    public async Task PatientCanReadOnlyOwnSummaryButNeverStaffChart()
    {
        var (client, visit) = await Setup();
        using (client) {
            var admitted = await client.PostAsJsonAsync("/api/clinical-cases/admit-from-visit", Request(visit));
            var id = (await admitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/patient/care-episodes")).StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
            var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();
            var patient = (await db.Users.FindAsync(visit.PatientId))!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", (await jwt.GenerateSecurityToken(patient.Username))!.AccessToken);
            var response = await client.GetAsync("/api/patient/care-episodes");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
            Assert.Single(rows); Assert.Equal(id, rows[0].GetProperty("id").GetGuid());
            Assert.False(rows[0].TryGetProperty("events", out _));
            Assert.False(rows[0].TryGetProperty("admissionReason", out _));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/clinical-cases/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/patient/care-episodes?page=0")).StatusCode);
            var other = NewUser(UserRole.Patient); db.Users.Add(other); await db.SaveChangesAsync();
            client.DefaultRequestHeaders.Authorization = new("Bearer", (await jwt.GenerateSecurityToken(other.Username))!.AccessToken);
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/patient/care-episodes")).EnumerateArray());
            client.DefaultRequestHeaders.Authorization = null;
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/patient/care-episodes")).StatusCode);
        }
    }

    [Fact]
    public async Task AdmissionThroughHttpKeepsSourceAndPatientScopeAndDischarges()
    {
        var (client, visit) = await Setup();
        using (client) {
            var response = await client.PostAsJsonAsync("/api/clinical-cases/admit-from-visit", Request(visit));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var c = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Visit", c.GetProperty("admissionSource").GetString());
            Assert.Equal(visit.Id, c.GetProperty("admissionAppointmentId").GetGuid());
            Assert.Equal(visit.PatientId, c.GetProperty("patientId").GetGuid());
            Assert.Equal(visit.HospitalId, c.GetProperty("hospitalId").GetGuid());
            var id = c.GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/clinical-cases/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/clinical-cases/{id}/events", new { version = 1, action = "Discharge", text = "Synthetic outcome" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/clinical-cases/admit-from-visit", Request(visit))).StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
            Assert.Null((await db.ClinicalCases.SingleAsync(x => x.Id == id)).AdmissionGrantId);
            Assert.Equal(AppointmentStatus.InProgress, (await db.Appointments.FindAsync(visit.Id))!.Status);
            Assert.False(await db.EmergencyAccessGrants.AnyAsync(g => g.PatientId == visit.PatientId));
            var missingSource = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ClinicalCases\" SET \"AdmissionAppointmentId\" = NULL WHERE \"Id\" = {id}"));
            Assert.Equal("23514", missingSource.SqlState);
            var secondSource = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ClinicalCases\" SET \"AdmissionGrantId\" = {Guid.NewGuid()} WHERE \"Id\" = {id}"));
            Assert.Equal("23514", secondSource.SqlState);
        }
    }

    [Fact]
    public async Task ConcurrentRequestsCreateExactlyOneCase()
    {
        var (client, visit) = await Setup();
        using (client) {
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/clinical-cases/admit-from-visit", Request(visit))));
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
            Assert.Equal(1, await db.ClinicalCases.CountAsync(c => c.AdmissionAppointmentId == visit.Id));
        }
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("patient", HttpStatusCode.Forbidden)]
    [InlineData("other-doctor", HttpStatusCode.NotFound)]
    [InlineData("cancelled", HttpStatusCode.BadRequest)]
    [InlineData("oversized", HttpStatusCode.BadRequest)]
    [InlineData("null-reason", HttpStatusCode.BadRequest)]
    public async Task HttpRejectsWrongActorStaleVisitAndMalformedInput(string scenario, HttpStatusCode expected)
    {
        var (client, visit) = await Setup();
        using (client) {
            if (scenario == "anonymous") client.DefaultRequestHeaders.Authorization = null;
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
            if (scenario == "cancelled") { (await db.Appointments.FindAsync(visit.Id))!.Status = AppointmentStatus.Cancelled; await db.SaveChangesAsync(); }
            if (scenario is "patient" or "other-doctor") {
                var user = scenario == "patient" ? await db.Users.FindAsync(visit.PatientId) : NewUser(UserRole.Doctor);
                if (scenario == "other-doctor") { db.Users.Add(user!); await db.SaveChangesAsync(); }
                var token = await scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateSecurityToken(user!.Username);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
            }
            var response = await client.PostAsJsonAsync("/api/clinical-cases/admit-from-visit", new {
                appointmentId = visit.Id, department = "Ward", bed = "", acceptResponsibility = true,
                reason = scenario == "null-reason" ? null : scenario == "oversized" ? new string('x', 1001) : "Test"
            });
            Assert.Equal(expected, response.StatusCode);
            Assert.False(await db.ClinicalCases.AnyAsync(c => c.AdmissionAppointmentId == visit.Id));
        }
    }

    private static object Request(Appointment a) => new { appointmentId = a.Id, department = "Ward", bed = "1", reason = "Synthetic ongoing care", acceptResponsibility = true };
    private static User NewUser(UserRole role) {
        var id = Guid.NewGuid();
        return new User { Id = id, Username = "visit-test-" + id, Email = id + "@example.invalid", FirstName = "Synthetic", LastName = "Visit test", Role = role,
            IsActive = true, IsEmailVerified = true, PasswordHash = "unused", PhoneNumber = "" };
    }
    private async Task<(HttpClient, Appointment)> Setup()
    {
        await factory.EnsureMigratedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        var doctor = NewUser(UserRole.Doctor); var patient = NewUser(UserRole.Patient);
        var hospital = new Hospital { Id = Guid.NewGuid(), Name = "Synthetic hospital", Address = "Test", City = "Baku", Country = "AZ", PhoneNumber = "", IsActive = true };
        db.Users.AddRange(doctor, patient); db.Hospitals.Add(hospital);
        db.DoctorProfiles.Add(new DoctorProfile { Id = Guid.NewGuid(), UserId = doctor.Id, LicenseNumber = "TEST-ONLY", LicenseExpiryDate = DateTime.UtcNow.AddYears(1), HospitalIds = [hospital.Id] });
        db.UserPermissions.Add(new UserPermission { Id = Guid.NewGuid(), UserId = doctor.Id, Permission = Permissions.ClinicalCaseAdmit, ResourceId = hospital.Id.ToString() });
        var visit = new Appointment { Id = Guid.NewGuid(), DoctorId = doctor.Id, PatientId = patient.Id, HospitalId = hospital.Id, Status = AppointmentStatus.InProgress, ScheduledAt = DateTime.UtcNow.AddHours(-1) };
        db.Appointments.Add(visit); await db.SaveChangesAsync();
        var token = await scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateSecurityToken(doctor.Username);
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return (client, visit);
    }
}
