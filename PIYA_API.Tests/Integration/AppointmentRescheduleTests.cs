using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Integration;

public sealed class AppointmentRescheduleTests(PiyaWebApplicationFactory factory) : IClassFixture<PiyaWebApplicationFactory>
{
    [Fact]
    public async Task ConcurrentMovesOfOneSourceCreateOnlyOneReplacement()
    {
        var (client, first, _) = await Setup(); using (client) {
            var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(i => client.PostAsJsonAsync(
                $"/api/appointment/{first.Id}/reschedule", new { newScheduledAt = first.ScheduledAt.AddHours(3 + i) })));
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
            var original = await db.Appointments.FindAsync(first.Id);
            Assert.Equal(AppointmentStatus.Rescheduled, original!.Status);
            Assert.NotNull(original.RescheduledToId);
            Assert.Equal(2, await db.Appointments.CountAsync(a => a.DoctorId == first.DoctorId && a.Status == AppointmentStatus.Scheduled));
        }
    }

    [Fact]
    public async Task ConcurrentMovesIntoOneSlotCannotDoubleBook()
    {
        var (client, first, second) = await Setup(); using (client) {
            var destination = first.ScheduledAt.AddHours(4);
            var responses = await Task.WhenAll(new[] { first, second }.Select(a => client.PostAsJsonAsync(
                $"/api/appointment/{a.Id}/reschedule", new { newScheduledAt = destination })));
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
            Assert.Equal(1, await db.Appointments.CountAsync(a => a.DoctorId == first.DoctorId && a.ScheduledAt == destination && a.Status == AppointmentStatus.Scheduled));
        }
    }

    [Fact]
    public async Task RescheduleReleasesOldSlotThroughHttpAndRejectsReusingHistory()
    {
        var (client, first, _) = await Setup(); using (client) {
            var moved = await client.PostAsJsonAsync($"/api/appointment/{first.Id}/reschedule", new { newScheduledAt = first.ScheduledAt.AddHours(3) });
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            var available = await client.GetFromJsonAsync<JsonElement>($"/api/appointment/doctor/{first.DoctorId}/availability?scheduledAt={Uri.EscapeDataString(first.ScheduledAt.ToString("O"))}");
            Assert.True(available.GetProperty("available").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/appointment/{first.Id}/reschedule", new { newScheduledAt = first.ScheduledAt.AddHours(4) })).StatusCode);
        }
    }

    private async Task<(HttpClient, Appointment, Appointment)> Setup()
    {
        await factory.EnsureMigratedAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        User Person(UserRole role) => new() { Id = Guid.NewGuid(), Username = "schedule_" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid()+"@example.test", FirstName = "Synthetic", LastName = "Schedule", PhoneNumber = "", IsActive = true, IsEmailVerified = true, Role = role };
        var patient = Person(UserRole.Patient); var doctor = Person(UserRole.Doctor);
        var hospital = new Hospital { Id = Guid.NewGuid(), Name = "Synthetic", Address = "Test", City = "Baku", Country = "AZ", PhoneNumber = "", IsActive = true };
        db.Users.AddRange(patient, doctor); db.Hospitals.Add(hospital);
        db.DoctorProfiles.Add(new DoctorProfile { Id = Guid.NewGuid(), UserId = doctor.Id, LicenseNumber = "TEST", LicenseExpiryDate = DateTime.UtcNow.AddYears(1), HospitalIds = [hospital.Id] });
        Appointment Booking(int hour) => new() { Id = Guid.NewGuid(), DoctorId = doctor.Id, PatientId = patient.Id, HospitalId = hospital.Id, ScheduledAt = DateTime.UtcNow.Date.AddDays(5).AddHours(hour), DurationMinutes = 30 };
        var first = Booking(5); var second = Booking(6); db.Appointments.AddRange(first, second); await db.SaveChangesAsync();
        var token = await scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateSecurityToken(patient.Username);
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", token!.AccessToken);
        return (client, first, second);
    }
}
