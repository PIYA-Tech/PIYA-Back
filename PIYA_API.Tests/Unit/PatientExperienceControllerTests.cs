using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Controllers;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public sealed class PatientExperienceControllerTests
{
    [Fact]
    public async Task UnconfiguredProvider_NeverClaimsPatientIsVerified()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient);
        db.Set<User>().Add(patient);
        await db.SaveChangesAsync();
        var audit = new Mock<IAuditService>();
        var controller = WithUser(new PatientVerificationController(
            db, new NotConnectedVerificationProviderGateway(), audit.Object,
            Mock.Of<ILogger<PatientVerificationController>>()), patient);

        var result = await controller.Start(
            PatientVerificationKind.Identity,
            new StartPatientVerificationRequest(true, "AZ", "National ID"),
            CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.Value.Should().BeOfType<PatientVerificationResponse>().Which.Status
            .Should().Be(PatientVerificationStatus.NotConnected);
        (await db.Set<PatientVerification>().SingleAsync()).VerifiedAt.Should().BeNull();
        audit.Verify(service => service.LogEntityActionAsync(
            "StartPatientVerification", nameof(PatientVerification), It.IsAny<string>(),
            patient.Id, It.Is<string>(message => message.Contains("NotConnected"))), Times.Once);
    }

    [Fact]
    public async Task IncompleteProviderResponse_IsDowngradedInsteadOfFakingVerification()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient);
        db.Set<User>().Add(patient);
        await db.SaveChangesAsync();
        var provider = new Mock<IVerificationProviderGateway>();
        provider.Setup(service => service.StartAsync(
                It.IsAny<VerificationProviderStartContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationProviderResult(
                PatientVerificationStatus.Verified, ProviderName: "Incomplete Provider"));
        var controller = WithUser(new PatientVerificationController(
            db, provider.Object, Mock.Of<IAuditService>(),
            Mock.Of<ILogger<PatientVerificationController>>()), patient);

        var result = await controller.Start(
            PatientVerificationKind.Identity,
            new StartPatientVerificationRequest(true, "AZ", "National ID"),
            CancellationToken.None);

        ((CreatedAtActionResult)result.Result!).Value
            .Should().BeOfType<PatientVerificationResponse>().Which.Status
            .Should().Be(PatientVerificationStatus.NotConnected);
        (await db.Set<PatientVerification>().SingleAsync()).StatusReasonCode
            .Should().Be("invalid_provider_response");
    }

    [Fact]
    public async Task CareCircle_RequiresMatchingInvitee_AndRevocationEndsConsent()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient, "patient@piya.life");
        var invitee = MakeUser(UserRole.Patient, "family@piya.life");
        db.Set<User>().AddRange(patient, invitee);
        await db.SaveChangesAsync();
        var audit = new Mock<IAuditService>();
        var patientController = WithUser(new CareCircleController(db, audit.Object), patient);

        var createResult = await patientController.CreateInvitation(
            new CreateCareCircleInvitationRequest(
                invitee.Email, CareCircleRole.Family,
                CareCircleScope.Appointments | CareCircleScope.Medications,
                DateTime.UtcNow.AddMonths(3)), CancellationToken.None);
        var created = createResult.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var invitation = created.Value.Should().BeOfType<CareCircleInvitationCreatedResponse>().Subject;

        var inviteeController = WithUser(new CareCircleController(db, audit.Object), invitee);
        var acceptResult = await inviteeController.AcceptInvitation(
            new AcceptCareCircleInvitationRequest(invitation.InvitationToken, true),
            CancellationToken.None);
        var member = ((OkObjectResult)acceptResult.Result!).Value
            .Should().BeOfType<CareCircleMemberResponse>().Subject;
        member.Scopes.Should().Be(CareCircleScope.Appointments | CareCircleScope.Medications);
        (await db.Set<CareCircleConsent>().SingleAsync()).Status.Should().Be(CareCircleConsentStatus.Active);
        var access = new CareCircleAccessService(db);
        (await access.HasScopeAsync(patient.Id, invitee.Id, CareCircleScope.Appointments)).Should().BeTrue();
        (await access.HasScopeAsync(patient.Id, invitee.Id, CareCircleScope.Documents)).Should().BeFalse();

        (await patientController.RevokeMember(
            member.Id, new RevokeCareCircleMemberRequest("No longer needed"),
            CancellationToken.None)).Should().BeOfType<NoContentResult>();
        (await db.Set<CareCircleMember>().SingleAsync()).Status.Should().Be(CareCircleMemberStatus.Revoked);
        (await db.Set<CareCircleConsent>().SingleAsync()).Status.Should().Be(CareCircleConsentStatus.Revoked);
        (await access.HasScopeAsync(patient.Id, invitee.Id, CareCircleScope.Appointments)).Should().BeFalse();
    }

    [Fact]
    public async Task CareCircle_WrongAccountCannotAcceptInvitation()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient, "patient@piya.life");
        var invitee = MakeUser(UserRole.Patient, "family@piya.life");
        var stranger = MakeUser(UserRole.Patient, "stranger@piya.life");
        db.Set<User>().AddRange(patient, invitee, stranger);
        await db.SaveChangesAsync();
        var audit = new Mock<IAuditService>();
        var patientController = WithUser(new CareCircleController(db, audit.Object), patient);
        var create = await patientController.CreateInvitation(
            new CreateCareCircleInvitationRequest(
                invitee.Email, CareCircleRole.Caregiver, CareCircleScope.CareTimeline),
            CancellationToken.None);
        var token = ((CareCircleInvitationCreatedResponse)((CreatedAtActionResult)create.Result!).Value!).InvitationToken;

        var strangerController = WithUser(new CareCircleController(db, audit.Object), stranger);
        var result = await strangerController.AcceptInvitation(
            new AcceptCareCircleInvitationRequest(token, true), CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
        (await db.Set<CareCircleMember>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DoctorCanPublishSummaryOnlyForOwnCompletedAppointment()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient);
        var doctor = MakeUser(UserRole.Doctor);
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id,
            HospitalId = Guid.NewGuid(), ScheduledAt = DateTime.UtcNow.AddDays(-1),
            Status = AppointmentStatus.Completed
        };
        db.Set<User>().AddRange(patient, doctor);
        db.Set<Appointment>().Add(appointment);
        await db.SaveChangesAsync();
        var inbox = new Mock<IPatientNotificationInboxService>();
        inbox.Setup(service => service.EnqueueAsync(
                It.IsAny<Guid>(), It.IsAny<PatientNotificationCategory>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientInboxNotification
            {
                Id = Guid.NewGuid(), UserId = patient.Id,
                Title = "Visit summary ready", Body = "Ready"
            });
        var controller = WithUser(new ContinuityOfCareController(
            db, Mock.Of<IAppointmentService>(), Mock.Of<IAuditService>(),
            inbox.Object,
            Mock.Of<ILogger<ContinuityOfCareController>>()), doctor);

        var draftResult = await controller.UpsertDraft(
            appointment.Id,
            new UpsertConsultationSummaryRequest(
                "Your consultation was completed.", CareInstructions: "Continue the agreed plan."),
            CancellationToken.None);
        var draft = ((OkObjectResult)draftResult.Result!).Value
            .Should().BeOfType<ConsultationSummaryResponse>().Subject;
        draft.Status.Should().Be(ConsultationSummaryStatus.Draft);

        var publishResult = await controller.Publish(draft.Id, CancellationToken.None);
        ((OkObjectResult)publishResult.Result!).Value
            .Should().BeOfType<ConsultationSummaryResponse>().Which.Status
            .Should().Be(ConsultationSummaryStatus.Published);
        inbox.Verify(service => service.EnqueueAsync(
            patient.Id, PatientNotificationCategory.Appointment, "Visit summary ready",
            It.IsAny<string>(), $"piya://health/summaries/{draft.Id}",
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            $"summary:{draft.Id}:published", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PatientSchedulesFollowUpInsideClinicianWindow()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient);
        var doctor = MakeUser(UserRole.Doctor);
        var hospitalId = Guid.NewGuid();
        var plan = new FollowUpPlan
        {
            Id = Guid.NewGuid(), ConsultationSummaryId = Guid.NewGuid(),
            SourceAppointmentId = Guid.NewGuid(), PatientId = patient.Id,
            DoctorId = doctor.Id, HospitalId = hospitalId, Reason = "Review progress",
            EarliestAt = DateTime.UtcNow.AddDays(5), LatestAt = DateTime.UtcNow.AddDays(10),
            Status = FollowUpStatus.Recommended
        };
        db.Set<User>().AddRange(patient, doctor);
        db.Set<FollowUpPlan>().Add(plan);
        await db.SaveChangesAsync();
        var appointmentService = new Mock<IAppointmentService>();
        appointmentService.Setup(service => service.BookAppointmentAsync(It.IsAny<Appointment>()))
            .ReturnsAsync((Appointment value) =>
            {
                value.Id = Guid.NewGuid();
                return value;
            });
        var controller = WithUser(new ContinuityOfCareController(
            db, appointmentService.Object, Mock.Of<IAuditService>(),
            Mock.Of<IPatientNotificationInboxService>(),
            Mock.Of<ILogger<ContinuityOfCareController>>()), patient);
        var requestedTime = DateTime.UtcNow.AddDays(7);

        var result = await controller.ScheduleFollowUp(
            plan.Id, new ScheduleFollowUpRequest(requestedTime), CancellationToken.None);

        ((OkObjectResult)result.Result!).Value.Should().BeOfType<FollowUpPlanResponse>()
            .Which.Status.Should().Be(FollowUpStatus.Scheduled);
        (await db.Set<FollowUpPlan>().SingleAsync()).ScheduledAppointmentId.Should().NotBeNull();
        appointmentService.Verify(service => service.BookAppointmentAsync(
            It.Is<Appointment>(item => item.PatientId == patient.Id && item.DoctorId == doctor.Id)), Times.Once);
    }

    [Fact]
    public async Task FinishingLastCareTask_CompletesClinicianAuthoredWorkflow()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient);
        var doctor = MakeUser(UserRole.Doctor);
        var workflow = new CareLoopWorkflow
        {
            Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id,
            Title = "Recovery check-ins", Source = "ClinicianAuthored", Status = CareLoopStatus.Active
        };
        var task = new CareLoopTask
        {
            Id = Guid.NewGuid(), WorkflowId = workflow.Id,
            Type = CareLoopTaskType.SymptomCheck, Title = "How are you feeling?",
            DueAt = DateTime.UtcNow.AddHours(-1), Status = CareLoopTaskStatus.Available
        };
        db.Set<CareLoopWorkflow>().Add(workflow);
        db.Set<CareLoopTask>().Add(task);
        await db.SaveChangesAsync();
        var service = new CareLoopService(db, Mock.Of<IAuditService>());

        var completed = await service.CompleteTaskAsync(
            patient.Id, task.Id, "Feeling better", CancellationToken.None);

        completed.Should().NotBeNull();
        completed!.Status.Should().Be(CareLoopTaskStatus.Completed);
        (await db.Set<CareLoopWorkflow>().SingleAsync()).Status.Should().Be(CareLoopStatus.Completed);
    }

    [Fact]
    public async Task ActivatingCarePlan_MakesItPatientVisibleAndCreatesInboxEvent()
    {
        await using var db = CreateContext();
        var patient = MakeUser(UserRole.Patient);
        var doctor = MakeUser(UserRole.Doctor);
        var workflow = new CareLoopWorkflow
        {
            Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id,
            Title = "Recovery check-ins", Source = "ClinicianAuthored", Status = CareLoopStatus.Draft
        };
        db.Set<User>().AddRange(patient, doctor);
        db.Set<CareLoopWorkflow>().Add(workflow);
        db.Set<CareLoopTask>().Add(new CareLoopTask
        {
            Id = Guid.NewGuid(), WorkflowId = workflow.Id,
            Type = CareLoopTaskType.SymptomCheck, Title = "Check in",
            DueAt = DateTime.UtcNow.AddDays(1), Status = CareLoopTaskStatus.Scheduled
        });
        await db.SaveChangesAsync();
        var inbox = new Mock<IPatientNotificationInboxService>();
        inbox.Setup(service => service.EnqueueAsync(
                It.IsAny<Guid>(), It.IsAny<PatientNotificationCategory>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientInboxNotification
            {
                Id = Guid.NewGuid(), UserId = patient.Id, Title = "Care plan ready", Body = "Ready"
            });
        var controller = WithUser(new CareLoopsController(
            db, Mock.Of<ICareLoopService>(), Mock.Of<IAuditService>(), inbox.Object,
            Mock.Of<ILogger<CareLoopsController>>()), doctor);

        var result = await controller.Activate(workflow.Id, CancellationToken.None);

        ((OkObjectResult)result.Result!).Value.Should().BeOfType<CareLoopWorkflowResponse>()
            .Which.Status.Should().Be(CareLoopStatus.Active);
        inbox.Verify(service => service.EnqueueAsync(
            patient.Id, PatientNotificationCategory.System, "Care plan ready", It.IsAny<string>(),
            $"piya://health/care-plans/{workflow.Id}",
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            $"care-loop:{workflow.Id}:activated", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new TestDbContext(options);
    }

    private static T WithUser<T>(T controller, User user) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Role, user.Role.ToString())
                ], "Test"))
            }
        };
        return controller;
    }

    private static User MakeUser(UserRole role, string? email = null) => new()
    {
        Id = Guid.NewGuid(), Username = Guid.NewGuid().ToString("N"), PasswordHash = "hash",
        FirstName = role.ToString(), LastName = "User",
        Email = email ?? $"{Guid.NewGuid():N}@piya.life", PhoneNumber = "+994501234567",
        Role = role, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private sealed class TestDbContext(DbContextOptions<PharmacyApiDbContext> options)
        : PharmacyApiDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigurePatientExperienceModels();
        }
    }
}
