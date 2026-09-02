using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/care-loops")]
[Authorize]
public sealed class CareLoopsController(
    PharmacyApiDbContext db,
    ICareLoopService careLoopService,
    IAuditService auditService,
    IPatientNotificationInboxService inbox,
    ILogger<CareLoopsController> logger) : ControllerBase
{
    private readonly PharmacyApiDbContext _db = db;
    private readonly ICareLoopService _careLoopService = careLoopService;
    private readonly IAuditService _auditService = auditService;
    private readonly IPatientNotificationInboxService _inbox = inbox;
    private readonly ILogger<CareLoopsController> _logger = logger;
    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpPost]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<CareLoopWorkflowResponse>> Create(
        [FromBody] CreateCareLoopWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null) return BadRequest(new { error = validationError });

        Appointment? appointment = null;
        ConsultationSummary? summary = null;
        if (request.AppointmentId.HasValue)
        {
            appointment = await _db.Set<Appointment>().AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == request.AppointmentId.Value && item.DoctorId == CurrentUserId,
                cancellationToken);
            if (appointment is null) return NotFound(new { error = "Source appointment not found." });
            if (appointment.Status != AppointmentStatus.Completed)
                return Conflict(new { error = "A care plan can only be created after a completed appointment." });
        }
        if (request.ConsultationSummaryId.HasValue)
        {
            summary = await _db.Set<ConsultationSummary>().AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == request.ConsultationSummaryId.Value && item.DoctorId == CurrentUserId &&
                item.Status == ConsultationSummaryStatus.Published, cancellationToken);
            if (summary is null) return NotFound(new { error = "Published consultation summary not found." });
        }
        var patientId = appointment?.PatientId ?? summary!.PatientId;
        if (appointment is not null && summary is not null &&
            (summary.PatientId != appointment.PatientId || summary.AppointmentId != appointment.Id))
            return BadRequest(new { error = "The appointment and consultation summary do not describe the same visit." });

        var now = DateTime.UtcNow;
        var workflow = new CareLoopWorkflow
        {
            Id = Guid.NewGuid(), PatientId = patientId, DoctorId = CurrentUserId,
            AppointmentId = appointment?.Id, ConsultationSummaryId = summary?.Id,
            Title = request.Title.Trim(), Description = Clean(request.Description),
            Source = "ClinicianAuthored", Status = CareLoopStatus.Draft,
            CreatedAt = now, UpdatedAt = now
        };
        var tasks = request.Tasks.Select(item => new CareLoopTask
        {
            Id = Guid.NewGuid(), WorkflowId = workflow.Id, Type = item.Type,
            Title = item.Title.Trim(), Instructions = Clean(item.Instructions),
            DueAt = item.DueAt, Status = CareLoopTaskStatus.Scheduled,
            CreatedAt = now, UpdatedAt = now
        }).ToList();
        _db.Set<CareLoopWorkflow>().Add(workflow);
        _db.Set<CareLoopTask>().AddRange(tasks);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "CreateCareLoopWorkflow", nameof(CareLoopWorkflow), workflow.Id.ToString(),
            CurrentUserId, "Doctor created a clinician-authored automated care plan draft");
        return CreatedAtAction(nameof(Get), new { id = workflow.Id },
            await BuildResponseAsync(workflow, tasks, cancellationToken));
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<IReadOnlyList<CareLoopWorkflowResponse>>> GetMine(
        [FromQuery] CareLoopStatus? status,
        CancellationToken cancellationToken)
    {
        await _careLoopService.RefreshPatientAsync(CurrentUserId, cancellationToken);
        var query = _db.Set<CareLoopWorkflow>().AsNoTracking()
            .Where(item => item.PatientId == CurrentUserId && item.Status != CareLoopStatus.Draft);
        if (status.HasValue) query = query.Where(item => item.Status == status.Value);
        var workflows = await query.OrderByDescending(item => item.UpdatedAt).Take(100)
            .ToListAsync(cancellationToken);
        return Ok(await BuildResponsesAsync(workflows, cancellationToken));
    }

    [HttpGet("doctor")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<IReadOnlyList<CareLoopWorkflowResponse>>> GetDoctorWorkflows(
        [FromQuery] CareLoopStatus? status,
        CancellationToken cancellationToken)
    {
        var query = _db.Set<CareLoopWorkflow>().AsNoTracking()
            .Where(item => item.DoctorId == CurrentUserId);
        if (status.HasValue) query = query.Where(item => item.Status == status.Value);
        var workflows = await query.OrderByDescending(item => item.UpdatedAt).Take(100)
            .ToListAsync(cancellationToken);
        return Ok(await BuildResponsesAsync(workflows, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CareLoopWorkflowResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var workflow = await _db.Set<CareLoopWorkflow>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (workflow is null) return NotFound(new { error = "Care plan not found." });
        var patientCanRead = workflow.PatientId == CurrentUserId && workflow.Status != CareLoopStatus.Draft;
        var doctorCanRead = workflow.DoctorId == CurrentUserId;
        var adminCanRead = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        if (!patientCanRead && !doctorCanRead && !adminCanRead) return Forbid();
        if (patientCanRead) await _careLoopService.RefreshPatientAsync(CurrentUserId, cancellationToken);
        var tasks = await _db.Set<CareLoopTask>().AsNoTracking()
            .Where(item => item.WorkflowId == workflow.Id).OrderBy(item => item.DueAt)
            .ToListAsync(cancellationToken);
        return Ok(await BuildResponseAsync(workflow, tasks, cancellationToken));
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = "Doctor")]
    public Task<ActionResult<CareLoopWorkflowResponse>> Activate(
        Guid id,
        CancellationToken cancellationToken) =>
        Transition(id, CareLoopStatus.Active, cancellationToken);

    [HttpPost("{id:guid}/pause")]
    [Authorize(Roles = "Doctor")]
    public Task<ActionResult<CareLoopWorkflowResponse>> Pause(
        Guid id,
        CancellationToken cancellationToken) =>
        Transition(id, CareLoopStatus.Paused, cancellationToken);

    [HttpPost("{id:guid}/resume")]
    [Authorize(Roles = "Doctor")]
    public Task<ActionResult<CareLoopWorkflowResponse>> Resume(
        Guid id,
        CancellationToken cancellationToken) =>
        Transition(id, CareLoopStatus.Active, cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Doctor")]
    public Task<ActionResult<CareLoopWorkflowResponse>> Cancel(
        Guid id,
        CancellationToken cancellationToken) =>
        Transition(id, CareLoopStatus.Cancelled, cancellationToken);

    [HttpPost("tasks/{taskId:guid}/complete")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<CareLoopTaskResponse>> CompleteTask(
        Guid taskId,
        [FromBody] CompleteCareLoopTaskRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.PatientResponse?.Length > 2000)
            return BadRequest(new { error = "Patient response is too long." });
        try
        {
            var task = await _careLoopService.CompleteTaskAsync(
                CurrentUserId, taskId, request?.PatientResponse, cancellationToken);
            return task is null
                ? NotFound(new { error = "Care task not found." })
                : Ok(ToTaskResponse(task));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpPost("tasks/{taskId:guid}/skip")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<CareLoopTaskResponse>> SkipTask(
        Guid taskId,
        [FromBody] SkipCareLoopTaskRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Reason?.Length > 500)
            return BadRequest(new { error = "Skip reason is too long." });
        try
        {
            var task = await _careLoopService.SkipTaskAsync(
                CurrentUserId, taskId, request?.Reason, cancellationToken);
            return task is null
                ? NotFound(new { error = "Care task not found." })
                : Ok(ToTaskResponse(task));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    private async Task<ActionResult<CareLoopWorkflowResponse>> Transition(
        Guid id,
        CareLoopStatus next,
        CancellationToken cancellationToken)
    {
        var workflow = await _db.Set<CareLoopWorkflow>().SingleOrDefaultAsync(item =>
            item.Id == id && item.DoctorId == CurrentUserId, cancellationToken);
        if (workflow is null) return NotFound(new { error = "Care plan not found." });
        if (!CanTransition(workflow.Status, next))
            return Conflict(new { error = $"A {workflow.Status} care plan cannot change to {next}." });
        var tasks = await _db.Set<CareLoopTask>()
            .Where(item => item.WorkflowId == workflow.Id).OrderBy(item => item.DueAt)
            .ToListAsync(cancellationToken);
        if (next == CareLoopStatus.Active && tasks.Count == 0)
            return Conflict(new { error = "Add at least one task before activating a care plan." });

        var now = DateTime.UtcNow;
        var firstActivation = next == CareLoopStatus.Active && !workflow.ActivatedAt.HasValue;
        workflow.Status = next;
        workflow.UpdatedAt = now;
        if (next == CareLoopStatus.Active)
        {
            workflow.ActivatedAt ??= now;
            workflow.PausedAt = null;
        }
        else if (next == CareLoopStatus.Paused) workflow.PausedAt = now;
        else if (next == CareLoopStatus.Cancelled)
        {
            workflow.CancelledAt = now;
            foreach (var task in tasks.Where(item => item.Status is not
                         (CareLoopTaskStatus.Completed or CareLoopTaskStatus.Skipped)))
            {
                task.Status = CareLoopTaskStatus.Cancelled;
                task.UpdatedAt = now;
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            $"{next}CareLoopWorkflow", nameof(CareLoopWorkflow), workflow.Id.ToString(),
            CurrentUserId, $"Doctor changed clinician-authored care plan to {next}");
        if (firstActivation)
            await NotifyPatientAsync(
                workflow.PatientId, workflow.Id, "Care plan ready",
                "Your doctor has shared a new care plan with you.",
                $"care-loop:{workflow.Id}:activated", cancellationToken);
        else if (next == CareLoopStatus.Cancelled)
            await NotifyPatientAsync(
                workflow.PatientId, workflow.Id, "Care plan closed",
                "Your doctor has closed a care plan.",
                $"care-loop:{workflow.Id}:cancelled", cancellationToken);
        return Ok(await BuildResponseAsync(workflow, tasks, cancellationToken));
    }

    private async Task<IReadOnlyList<CareLoopWorkflowResponse>> BuildResponsesAsync(
        IReadOnlyList<CareLoopWorkflow> workflows,
        CancellationToken cancellationToken)
    {
        if (workflows.Count == 0) return [];
        var workflowIds = workflows.Select(item => item.Id).ToArray();
        var tasks = await _db.Set<CareLoopTask>().AsNoTracking()
            .Where(item => workflowIds.Contains(item.WorkflowId)).OrderBy(item => item.DueAt)
            .ToListAsync(cancellationToken);
        var doctorIds = workflows.Select(item => item.DoctorId).Distinct().ToArray();
        var doctors = await _db.Set<User>().AsNoTracking()
            .Where(item => doctorIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        return workflows.Select(workflow =>
        {
            doctors.TryGetValue(workflow.DoctorId, out var doctor);
            return ToWorkflowResponse(workflow,
                tasks.Where(item => item.WorkflowId == workflow.Id).ToList(), doctor);
        }).ToList();
    }

    private async Task<CareLoopWorkflowResponse> BuildResponseAsync(
        CareLoopWorkflow workflow,
        IReadOnlyList<CareLoopTask> tasks,
        CancellationToken cancellationToken)
    {
        var doctor = await _db.Set<User>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == workflow.DoctorId, cancellationToken);
        return ToWorkflowResponse(workflow, tasks, doctor);
    }

    private static CareLoopWorkflowResponse ToWorkflowResponse(
        CareLoopWorkflow workflow,
        IReadOnlyList<CareLoopTask> tasks,
        User? doctor) => new(
        workflow.Id, workflow.PatientId, workflow.DoctorId,
        doctor is null ? "Doctor" : $"Dr. {doctor.FirstName} {doctor.LastName}".Trim(),
        workflow.AppointmentId, workflow.ConsultationSummaryId,
        workflow.Title, workflow.Description, workflow.Source, workflow.Status,
        workflow.ActivatedAt, workflow.CompletedAt, workflow.UpdatedAt,
        tasks.Select(ToTaskResponse).ToList());

    private static CareLoopTaskResponse ToTaskResponse(CareLoopTask task) => new(
        task.Id, task.Type, task.Title, task.Instructions, task.DueAt, task.Status,
        task.PatientResponse, task.CompletedAt, task.SkippedAt, task.SkipReason);

    private static bool CanTransition(CareLoopStatus current, CareLoopStatus next) =>
        (current, next) switch
        {
            (CareLoopStatus.Draft, CareLoopStatus.Active) => true,
            (CareLoopStatus.Draft, CareLoopStatus.Cancelled) => true,
            (CareLoopStatus.Active, CareLoopStatus.Paused) => true,
            (CareLoopStatus.Active, CareLoopStatus.Cancelled) => true,
            (CareLoopStatus.Paused, CareLoopStatus.Active) => true,
            (CareLoopStatus.Paused, CareLoopStatus.Cancelled) => true,
            _ => false
        };

    private static string? Validate(CreateCareLoopWorkflowRequest request)
    {
        if (!request.AppointmentId.HasValue && !request.ConsultationSummaryId.HasValue)
            return "A completed appointment or published consultation summary is required.";
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
            return "A care plan title of at most 200 characters is required.";
        if (request.Description?.Length > 2000) return "Care plan description is too long.";
        if (request.Tasks is null || request.Tasks.Count is < 1 or > 50)
            return "A care plan must contain between 1 and 50 tasks.";
        foreach (var task in request.Tasks)
        {
            if (!Enum.IsDefined(task.Type)) return "A care task has an unsupported type.";
            if (string.IsNullOrWhiteSpace(task.Title) || task.Title.Length > 200)
                return "Every care task requires a title of at most 200 characters.";
            if (task.Instructions?.Length > 2000) return "Care task instructions are too long.";
            if (task.DueAt <= DateTime.UtcNow.AddMinutes(-1) || task.DueAt > DateTime.UtcNow.AddYears(1))
                return "Every care task must be due within the next year.";
        }
        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task NotifyPatientAsync(
        Guid patientId,
        Guid workflowId,
        string title,
        string body,
        string dedupeKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _inbox.EnqueueAsync(
                patientId, PatientNotificationCategory.System, title, body,
                $"piya://health/care-plans/{workflowId}",
                new Dictionary<string, string> { ["type"] = "care_loop" },
                dedupeKey, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "Care plan saved but inbox notification {DedupeKey} failed", dedupeKey);
        }
    }
}
