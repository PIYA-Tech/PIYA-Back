using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Controllers;
using PIYA_API.Middleware;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class ConferenceDoctorTests
{
    [Theory]
    [InlineData("Auth", "Me", "GET", true)]
    [InlineData("Auth", "RefreshToken", "POST", true)]
    [InlineData("Auth", "Logout", "POST", true)]
    [InlineData("DoctorDashboard", "GetMyAppointments", "GET", true)]
    [InlineData("DoctorDashboard", "CreatePrescription", "POST", true)]
    [InlineData("DoctorDashboard", "GetPatientRecords", "GET", true)]
    [InlineData("Medication", "GetAll", "GET", true)]
    [InlineData("Medication", "GetAllAdmin", "GET", false)]
    [InlineData("DoctorDashboard", "UpdateProfile", "PUT", false)]
    [InlineData("DoctorDashboard", "SetOnline", "POST", false)]
    [InlineData("DoctorDashboard", "GetMyPatients", "GET", false)]
    [InlineData("DoctorNote", "CreateNote", "POST", false)]
    [InlineData("EmergencyAccess", "RequestAccess", "POST", true)]
    [InlineData("EmergencyAccess", "GetGrant", "GET", true)]
    [InlineData("EmergencyAccess", "GenerateShareToken", "POST", false)]
    [InlineData("User", "UpdateUser", "PUT", false)]
    [InlineData("Auth", "ChangePassword", "POST", false)]
    [InlineData("FutureController", "FutureAction", "GET", false)]
    [InlineData("DoctorDashboard", "GetMyAppointments", "POST", false)]
    public async Task Middleware_UsesDenyByDefaultEndpointAllowlist(
        string controller, string action, string method, bool allowed)
    {
        var context = Context(ConferenceDoctorScope.DoctorId);
        context.Request.Method = method;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new ControllerActionDescriptor
            { ControllerName = controller, ActionName = action }), "test"));
        var called = false;
        await new ConferenceDoctorMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);
        Assert.Equal(allowed, called);
        if (!allowed) Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task HubsAndUnmappedRoutes_AreDenied()
    {
        var context = Context(ConferenceDoctorScope.DoctorId);
        context.Request.Path = "/hubs/inventory";
        await new ConferenceDoctorMiddleware(_ => throw new Exception("Must not run")).InvokeAsync(context);
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task OrdinaryDoctors_AreUnaffected()
    {
        var called = false;
        await new ConferenceDoctorMiddleware(_ => { called = true; return Task.CompletedTask; })
            .InvokeAsync(Context(Guid.NewGuid()));
        Assert.True(called);
    }

    [Fact]
    public async Task PatientRecords_RealPatientDeniedBeforeQuery()
    {
        var appointments = new Mock<IAppointmentService>(MockBehavior.Strict);
        var controller = Controller(appointments);
        Assert.IsType<ForbidResult>(await controller.GetPatientRecords(Guid.NewGuid()));
        appointments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RealPatientPrescription_DeniedBeforePermissionOrWrite()
    {
        var controller = Controller(new Mock<IAppointmentService>(MockBehavior.Strict));
        var result = await controller.CreatePrescription(new CreatePrescriptionRequest { PatientId = Guid.NewGuid() });
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("complete")]
    [InlineData("cancel")]
    public async Task EvenAssignedRealAppointment_CannotBeChanged(string operation)
    {
        var appointment = new Appointment { Id = Guid.NewGuid(), DoctorId = ConferenceDoctorScope.DoctorId,
            PatientId = Guid.NewGuid(), ScheduledAt = DateTime.UtcNow };
        var service = new Mock<IAppointmentService>(MockBehavior.Strict);
        service.Setup(x => x.GetByIdAsync(appointment.Id, default)).ReturnsAsync(appointment);
        var controller = Controller(service);
        var result = operation switch
        {
            "start" => await controller.StartAppointment(appointment.Id),
            "complete" => await controller.CompleteAppointment(appointment.Id),
            _ => await controller.CancelAppointment(appointment.Id)
        };
        Assert.IsType<ForbidResult>(result.Result);
        service.Verify(x => x.GetByIdAsync(appointment.Id, default), Times.Once);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public void Scope_IsOnlyTheFixedFictionalPatient()
    {
        Assert.True(ConferenceDoctorScope.CanAccessPatient(ConferenceDoctorScope.DoctorId, ConferenceDoctorScope.PatientId));
        Assert.False(ConferenceDoctorScope.CanAccessPatient(ConferenceDoctorScope.DoctorId, Guid.NewGuid()));
    }

    [Fact]
    public async Task AppointmentList_ExcludesAccidentallyAssignedRealPatients()
    {
        var service = new Mock<IAppointmentService>();
        service.Setup(x => x.GetDoctorAppointmentsAsync(ConferenceDoctorScope.DoctorId, null, null, default))
            .ReturnsAsync([
                new Appointment { Id = Guid.NewGuid(), PatientId = ConferenceDoctorScope.PatientId, ScheduledAt = DateTime.UtcNow },
                new Appointment { Id = Guid.NewGuid(), PatientId = Guid.NewGuid(), ScheduledAt = DateTime.UtcNow }
            ]);
        var result = await Controller(service).GetMyAppointments();
        var rows = Assert.IsAssignableFrom<IEnumerable<PIYA_API.DTOs.AppointmentResponseDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(rows);
        Assert.Equal(ConferenceDoctorScope.PatientId, rows.Single().PatientId);
    }

    [Fact]
    public async Task FictionalAppointment_CanBeStarted()
    {
        var appointment = new Appointment { Id = Guid.NewGuid(), DoctorId = ConferenceDoctorScope.DoctorId,
            PatientId = ConferenceDoctorScope.PatientId, ScheduledAt = DateTime.UtcNow };
        var service = new Mock<IAppointmentService>();
        service.Setup(x => x.GetByIdAsync(appointment.Id, default)).ReturnsAsync(appointment);
        service.Setup(x => x.UpdateStatusAsync(appointment.Id, AppointmentStatus.InProgress, default))
            .ReturnsAsync(appointment);
        Assert.IsType<OkObjectResult>((await Controller(service).StartAppointment(appointment.Id)).Result);
    }

    private static DefaultHttpContext Context(Guid id)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, "Doctor")], "test"));
        return context;
    }

    private static DoctorDashboardController Controller(Mock<IAppointmentService> appointments) => new(
        Mock.Of<IDoctorProfileService>(), appointments.Object, Mock.Of<IPrescriptionService>(),
        Mock.Of<IPermissionService>(), Mock.Of<ILogger<DoctorDashboardController>>())
        { ControllerContext = new ControllerContext { HttpContext = Context(ConferenceDoctorScope.DoctorId) } };
}
