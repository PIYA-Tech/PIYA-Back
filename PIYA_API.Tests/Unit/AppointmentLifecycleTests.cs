using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public sealed class AppointmentLifecycleTests
{
    [Fact]
    public async Task RescheduleReleasesOldSlotAndOnlyTheReplacementCanMoveAgain()
    {
        await using var db = Database();
        var original = await Seed(db);
        var service = Service(db);
        var replacement = await service.RescheduleAppointmentAsync(original.Id, original.ScheduledAt.AddHours(1));
        Assert.True(await service.IsDoctorAvailableAsync(original.DoctorId, original.ScheduledAt));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RescheduleAppointmentAsync(original.Id, original.ScheduledAt.AddHours(2)));
        var next = await service.RescheduleAppointmentAsync(replacement.Id, original.ScheduledAt.AddHours(2));
        Assert.True(await service.IsDoctorAvailableAsync(original.DoctorId, replacement.ScheduledAt));
        Assert.Equal(next.Id, replacement.RescheduledToId);
        Assert.Single(await db.Appointments.Where(a => a.Status == AppointmentStatus.Scheduled).ToListAsync());
    }

    [Theory]
    [InlineData(AppointmentStatus.Rescheduled)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task TerminalHistoryCannotBeRestarted(AppointmentStatus status)
    {
        await using var db = Database();
        var original = await Seed(db); original.Status = status; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).UpdateStatusAsync(original.Id, AppointmentStatus.InProgress));
    }

    [Fact]
    public async Task ReschedulingRevalidatesLicenseAtTheNewTime()
    {
        await using var db = Database(); var original = await Seed(db);
        var profile = await db.DoctorProfiles.SingleAsync();
        profile.LicenseExpiryDate = original.ScheduledAt.AddHours(1); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).RescheduleAppointmentAsync(original.Id, original.ScheduledAt.AddDays(1)));
        Assert.Equal(AppointmentStatus.Scheduled, original.Status);
        Assert.Single(await db.Appointments.ToListAsync());
    }

    [Fact]
    public async Task ConfiguredBakuWorkingHoursIncludeTheEntireAppointment()
    {
        await using var db = Database(); var original = await Seed(db);
        var local = TimeZoneInfo.ConvertTimeFromUtc(original.ScheduledAt, TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku"));
        var profile = await db.DoctorProfiles.SingleAsync();
        profile.WorkingHours = JsonSerializer.Serialize(new[] { new WorkingHoursSlot {
            DayOfWeek = local.DayOfWeek.ToString(), Slots = [new TimeSlot { Start = "09:00", End = "10:00" }] } });
        await db.SaveChangesAsync();
        // 05:45 UTC is 09:45 Baku: a 30 minute appointment would overrun the shift.
        var outside = original.ScheduledAt.Date.AddHours(5).AddMinutes(45);
        Assert.False(await Service(db).IsDoctorAvailableAsync(original.DoctorId, outside, 30));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).RescheduleAppointmentAsync(original.Id, outside));
    }

    private static PharmacyApiDbContext Database() => new(new DbContextOptionsBuilder<PharmacyApiDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static AppointmentService Service(PharmacyApiDbContext db) => new(db, Mock.Of<IAuditService>(), Mock.Of<IEmailService>(), Mock.Of<ILogger<AppointmentService>>());
    private static async Task<Appointment> Seed(PharmacyApiDbContext db)
    {
        User Person(UserRole role) => new() { Id = Guid.NewGuid(), Username = Guid.NewGuid().ToString(), FirstName = "Synthetic", LastName = "Test", PhoneNumber = "", Email = Guid.NewGuid()+"@example.test", IsActive = true, Role = role };
        var doctor = Person(UserRole.Doctor); var patient = Person(UserRole.Patient);
        var hospital = new Hospital { Id = Guid.NewGuid(), Name = "Synthetic", Address = "Test", City = "Baku", Country = "AZ", PhoneNumber = "", IsActive = true };
        db.Users.AddRange(doctor, patient); db.Hospitals.Add(hospital);
        db.DoctorProfiles.Add(new DoctorProfile { Id = Guid.NewGuid(), UserId = doctor.Id, LicenseNumber = "TEST", LicenseExpiryDate = DateTime.UtcNow.AddYears(1), HospitalIds = [hospital.Id] });
        var appointment = new Appointment { Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id, HospitalId = hospital.Id, ScheduledAt = DateTime.UtcNow.Date.AddDays(5).AddHours(5), DurationMinutes = 30 };
        db.Appointments.Add(appointment); await db.SaveChangesAsync(); return appointment;
    }
}
