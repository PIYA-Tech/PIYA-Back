using System.Collections;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Controllers;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public sealed class AppointmentScheduleVisibilityTests
{
    [Theory]
    [InlineData(AppointmentStatus.Scheduled, true)]
    [InlineData(AppointmentStatus.Confirmed, true)]
    [InlineData(AppointmentStatus.Rescheduled, true)]
    [InlineData(AppointmentStatus.Cancelled, false)]
    [InlineData(AppointmentStatus.Completed, false)]
    [InlineData(AppointmentStatus.NoShow, false)]
    [InlineData(AppointmentStatus.InProgress, false)]
    public async Task Schedule_OnlyIncludesStatusesThatBlockAvailability(AppointmentStatus status, bool expected)
    {
        var appointment = MakeAppointment(status);
        var service = ServiceReturning(appointment);
        var controller = Controller(service);

        var response = await controller.GetDoctorSchedule(appointment.DoctorId, appointment.ScheduledAt.Date);

        var rows = Rows(response);
        rows.Should().HaveCount(expected ? 1 : 0);
    }

    [Fact]
    public async Task CancellingAppointment_ReleasesPreviouslyOccupiedScheduleSlot()
    {
        var appointment = MakeAppointment(AppointmentStatus.Confirmed);
        var service = ServiceReturning(appointment);
        var controller = Controller(service);

        Rows(await controller.GetDoctorSchedule(appointment.DoctorId, appointment.ScheduledAt.Date))
            .Should().ContainSingle();
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancelledAt = DateTime.UtcNow;

        Rows(await controller.GetDoctorSchedule(appointment.DoctorId, appointment.ScheduledAt.Date))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Schedule_ExposesOnlyTimeAndDurationNeverPatientDetails()
    {
        var appointment = MakeAppointment(AppointmentStatus.Scheduled);
        var controller = Controller(ServiceReturning(appointment));

        var row = Rows(await controller.GetDoctorSchedule(appointment.DoctorId, appointment.ScheduledAt.Date))
            .Should().ContainSingle().Which;

        row.GetType().GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(["ScheduledAt", "DurationMinutes"]);
        row.GetType().GetProperty("ScheduledAt")!.GetValue(row).Should().Be(appointment.ScheduledAt);
        row.GetType().GetProperty("DurationMinutes")!.GetValue(row).Should().Be(appointment.DurationMinutes);
    }

    private static List<object> Rows(ActionResult<List<object>> response)
    {
        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        return ((IEnumerable)ok.Value!).Cast<object>().ToList();
    }

    private static Mock<IAppointmentService> ServiceReturning(Appointment appointment)
    {
        var service = new Mock<IAppointmentService>();
        service.Setup(item => item.GetDoctorAppointmentsAsync(
            appointment.DoctorId, appointment.ScheduledAt.Date, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([appointment]);
        return service;
    }

    private static AppointmentController Controller(Mock<IAppointmentService> service) => new(
        service.Object, Mock.Of<IUserService>(), Mock.Of<ILogger<AppointmentController>>());

    private static Appointment MakeAppointment(AppointmentStatus status) => new()
    {
        Id = Guid.NewGuid(), DoctorId = Guid.NewGuid(), PatientId = Guid.NewGuid(),
        HospitalId = Guid.NewGuid(), ScheduledAt = DateTime.UtcNow.Date.AddDays(1).AddHours(9),
        DurationMinutes = 30, Status = status, Reason = "Private medical reason"
    };
}
