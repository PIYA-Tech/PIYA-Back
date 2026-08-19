using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class NotificationAndReminderSafetyTests
{
    [Fact]
    public async Task NotificationService_DisabledEmail_DoesNotReportSuccess()
    {
        var email = new Mock<IEmailService>();
        var service = CreateNotificationService(
            email: email.Object,
            configuration: new ConfigurationBuilder().Build());

        var sent = await service.SendEmailAsync(
            "patient@example.test",
            "Sensitive subject",
            "Sensitive body");

        sent.Should().BeFalse();
        email.Verify(
            candidate => candidate.SendEmailAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task NotificationService_DelegatesToConfiguredProviders_AndPropagatesFailure()
    {
        var email = new Mock<IEmailService>();
        email.Setup(candidate => candidate.SendEmailAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        var sms = new Mock<ISmsService>();
        sms.Setup(candidate => candidate.SendSmsAsync(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(false);
        var fcm = new Mock<IFcmService>();
        fcm.Setup(candidate => candidate.SendToUserAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>()))
            .ReturnsAsync(0);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalApis:EmailService:Enabled"] = "true",
                ["ExternalApis:EmailService:SmtpUsername"] = "configured-user"
            })
            .Build();
        var service = CreateNotificationService(
            email.Object,
            sms.Object,
            fcm.Object,
            configuration);

        (await service.SendEmailAsync(
            "patient@example.test",
            "Subject",
            "Plain text",
            isHtml: false)).Should().BeTrue();
        (await service.SendSmsAsync("+15555550100", "Message")).Should().BeFalse();
        (await service.SendPushNotificationAsync(
            Guid.NewGuid(),
            "Title",
            "Body")).Should().BeFalse();

        email.Verify(candidate => candidate.SendEmailAsync(
            "patient@example.test",
            "Subject",
            "Plain text",
            "Plain text"));
    }

    [Fact]
    public async Task AppointmentReminder_PartialDelivery_RemainsPendingForRetry()
    {
        await using var context = CreateContext();
        var (patient, appointment) = await SeedAppointmentAsync(context);
        var reminder = new AppointmentReminder
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id,
            UserId = patient.Id,
            ReminderTime = DateTime.UtcNow.AddMinutes(-1),
            MinutesBeforeAppointment = 60,
            DeliveryMethods =
            [
                ReminderDeliveryMethod.Email,
                ReminderDeliveryMethod.SMS
            ]
        };
        context.AppointmentReminders.Add(reminder);
        await context.SaveChangesAsync();

        var notifications = new Mock<INotificationService>();
        notifications.Setup(candidate => candidate.SendEmailAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>()))
            .ReturnsAsync(true);
        notifications.Setup(candidate => candidate.SendSmsAsync(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(false);
        var service = new AppointmentReminderService(
            context,
            notifications.Object,
            Mock.Of<ISignalRNotificationService>(),
            Mock.Of<ILogger<AppointmentReminderService>>());

        (await service.ProcessPendingRemindersAsync()).Should().Be(0);

        reminder.IsSent.Should().BeFalse();
        reminder.SentAt.Should().BeNull();
        reminder.RetryCount.Should().Be(1);
        reminder.DeliveryStatus.Should().Contain("Email: Sent");
        reminder.DeliveryStatus.Should().Contain("SMS: Failed");
    }

    [Fact]
    public async Task RefillReminder_FailedDelivery_RemainsPendingForRetry()
    {
        await using var context = CreateContext();
        var (patient, prescription) = await SeedPrescriptionAsync(context);
        var reminder = new PrescriptionRefillReminder
        {
            Id = Guid.NewGuid(),
            PrescriptionId = prescription.Id,
            PatientId = patient.Id,
            ReminderDate = DateTime.UtcNow.AddMinutes(-1),
            EstimatedRefillDate = DateTime.UtcNow.AddDays(3),
            DaysBeforeRefill = 3,
            DeliveryMethods = [ReminderDeliveryMethod.Email]
        };
        context.PrescriptionRefillReminders.Add(reminder);
        await context.SaveChangesAsync();

        var notifications = new Mock<INotificationService>();
        notifications.Setup(candidate => candidate.SendEmailAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>()))
            .ReturnsAsync(false);
        var service = new PrescriptionRefillReminderService(
            context,
            notifications.Object,
            Mock.Of<ISignalRNotificationService>(),
            Mock.Of<ILogger<PrescriptionRefillReminderService>>());

        (await service.ProcessPendingRefillRemindersAsync()).Should().Be(0);

        reminder.IsSent.Should().BeFalse();
        reminder.SentAt.Should().BeNull();
        reminder.RetryCount.Should().Be(1);
    }

    private static NotificationService CreateNotificationService(
        IEmailService? email = null,
        ISmsService? sms = null,
        IFcmService? fcm = null,
        IConfiguration? configuration = null) =>
        new(
            configuration ?? new ConfigurationBuilder().Build(),
            email ?? Mock.Of<IEmailService>(),
            sms ?? Mock.Of<ISmsService>(),
            fcm ?? Mock.Of<IFcmService>(),
            Mock.Of<ILogger<NotificationService>>());

    private static PharmacyApiDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PharmacyApiDbContext(options);
    }

    private static async Task<(User Patient, Appointment Appointment)> SeedAppointmentAsync(
        PharmacyApiDbContext context)
    {
        var patient = CreateUser("patient");
        var doctor = CreateUser("doctor", UserRole.Doctor);
        var hospital = new Hospital
        {
            Id = Guid.NewGuid(),
            Name = "Test Hospital",
            Address = "1 Test Street",
            City = "Test City",
            Country = "Test Country",
            PhoneNumber = "+15555550111"
        };
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            HospitalId = hospital.Id,
            ScheduledAt = DateTime.UtcNow.AddHours(1)
        };
        context.AddRange(patient, doctor, hospital, appointment);
        await context.SaveChangesAsync();
        return (patient, appointment);
    }

    private static async Task<(User Patient, Prescription Prescription)> SeedPrescriptionAsync(
        PharmacyApiDbContext context)
    {
        var patient = CreateUser("refill-patient");
        var doctor = CreateUser("refill-doctor", UserRole.Doctor);
        var prescription = new Prescription
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        context.AddRange(patient, doctor, prescription);
        await context.SaveChangesAsync();
        return (patient, prescription);
    }

    private static User CreateUser(
        string username,
        UserRole role = UserRole.Patient) =>
        new()
        {
            Id = Guid.NewGuid(),
            Username = username,
            FirstName = "Test",
            LastName = "User",
            Email = $"{username}@example.test",
            PhoneNumber = "+15555550100",
            Role = role
        };
}
