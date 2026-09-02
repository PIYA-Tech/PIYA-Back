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
[Route("api/continuity-of-care")]
[Authorize]
public sealed class ContinuityOfCareController(
    PharmacyApiDbContext db,
    IAppointmentService appointmentService,
    IAuditService auditService,
    IPatientNotificationInboxService inbox,
    ILogger<ContinuityOfCareController> logger) : ControllerBase
{
    private readonly PharmacyApiDbContext _db = db;
    private readonly IAppointmentService _appointmentService = appointmentService;
    private readonly IAuditService _auditService = auditService;
    private readonly IPatientNotificationInboxService _inbox = inbox;
    private readonly ILogger<ContinuityOfCareController> _logger = logger;
    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet("summaries/mine")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<IReadOnlyList<ConsultationSummaryResponse>>> GetMySummaries(
        CancellationToken cancellationToken)
    {
        var summaries = await _db.Set<ConsultationSummary>().AsNoTracking()
            .Where(item => item.PatientId == CurrentUserId && item.Status == ConsultationSummaryStatus.Published)
            .OrderByDescending(item => item.PublishedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return Ok(await BuildSummaryResponsesAsync(summaries, cancellationToken));
    }

    [HttpGet("summaries/doctor")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<IReadOnlyList<ConsultationSummaryResponse>>> GetDoctorSummaries(
        [FromQuery] ConsultationSummaryStatus? status,
        CancellationToken cancellationToken)
    {
        var query = _db.Set<ConsultationSummary>().AsNoTracking()
            .Where(item => item.DoctorId == CurrentUserId);
        if (status.HasValue) query = query.Where(item => item.Status == status.Value);
        var summaries = await query.OrderByDescending(item => item.UpdatedAt).Take(100)
            .ToListAsync(cancellationToken);
        return Ok(await BuildSummaryResponsesAsync(summaries, cancellationToken));
    }

    [HttpGet("summaries/{id:guid}")]
    public async Task<ActionResult<ConsultationSummaryResponse>> GetSummary(
        Guid id,
        CancellationToken cancellationToken)
    {
        var summary = await _db.Set<ConsultationSummary>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (summary is null) return NotFound(new { error = "Consultation summary not found." });
        var isPatient = summary.PatientId == CurrentUserId && summary.Status == ConsultationSummaryStatus.Published;
        var isDoctor = summary.DoctorId == CurrentUserId;
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        if (!isPatient && !isDoctor && !isAdmin) return Forbid();
        return Ok(await BuildSummaryResponseAsync(summary, cancellationToken));
    }

    [HttpPut("appointments/{appointmentId:guid}/summary")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<ConsultationSummaryResponse>> UpsertDraft(
        Guid appointmentId,
        [FromBody] UpsertConsultationSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var contentError = ValidateSummary(request.Summary, request.Diagnosis,
            request.CareInstructions, request.WarningSigns, request.PatientMessage);
        if (contentError is not null) return BadRequest(new { error = contentError });
        var appointment = await _db.Set<Appointment>().AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == appointmentId && item.DoctorId == CurrentUserId, cancellationToken);
        if (appointment is null) return NotFound(new { error = "Completed appointment not found." });
        if (appointment.Status != AppointmentStatus.Completed)
            return Conflict(new { error = "A consultation summary can only be written after the visit is completed." });

        var summary = await _db.Set<ConsultationSummary>()
            .SingleOrDefaultAsync(item => item.AppointmentId == appointment.Id, cancellationToken);
        if (summary is not null && summary.Status == ConsultationSummaryStatus.Published)
            return Conflict(new { error = "Published summaries must be amended through the amendment endpoint." });
        var now = DateTime.UtcNow;
        if (summary is null)
        {
            summary = new ConsultationSummary
            {
                Id = Guid.NewGuid(), AppointmentId = appointment.Id,
                PatientId = appointment.PatientId, DoctorId = appointment.DoctorId,
                Status = ConsultationSummaryStatus.Draft, CreatedAt = now
            };
            _db.Set<ConsultationSummary>().Add(summary);
        }
        ApplySummary(summary, request.Summary, request.Diagnosis, request.CareInstructions,
            request.WarningSigns, request.PatientMessage, now);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "SaveConsultationSummaryDraft", nameof(ConsultationSummary), summary.Id.ToString(),
            CurrentUserId, "Doctor saved a post-consultation summary draft");
        return Ok(await BuildSummaryResponseAsync(summary, cancellationToken));
    }

    [HttpPost("summaries/{id:guid}/publish")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<ConsultationSummaryResponse>> Publish(
        Guid id,
        CancellationToken cancellationToken)
    {
        var summary = await _db.Set<ConsultationSummary>().SingleOrDefaultAsync(item =>
            item.Id == id && item.DoctorId == CurrentUserId, cancellationToken);
        if (summary is null) return NotFound(new { error = "Consultation summary not found." });
        if (summary.Status == ConsultationSummaryStatus.Published)
            return Ok(await BuildSummaryResponseAsync(summary, cancellationToken));
        if (string.IsNullOrWhiteSpace(summary.Summary))
            return Conflict(new { error = "A summary cannot be published without clinical content." });
        summary.Status = ConsultationSummaryStatus.Published;
        summary.PublishedAt = DateTime.UtcNow;
        summary.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "PublishConsultationSummary", nameof(ConsultationSummary), summary.Id.ToString(),
            CurrentUserId, "Doctor published a consultation summary to the patient");
        await NotifyPatientAsync(
            summary.PatientId, PatientNotificationCategory.Appointment,
            "Visit summary ready", "Your doctor has published a summary of your completed visit.",
            $"piya://health/summaries/{summary.Id}", $"summary:{summary.Id}:published", cancellationToken);
        return Ok(await BuildSummaryResponseAsync(summary, cancellationToken));
    }

    [HttpPost("summaries/{id:guid}/amend")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<ConsultationSummaryResponse>> Amend(
        Guid id,
        [FromBody] AmendConsultationSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var contentError = ValidateSummary(request.Summary, request.Diagnosis,
            request.CareInstructions, request.WarningSigns, request.PatientMessage);
        if (contentError is not null) return BadRequest(new { error = contentError });
        if (string.IsNullOrWhiteSpace(request.AmendmentReason) || request.AmendmentReason.Length > 500)
            return BadRequest(new { error = "A concise amendment reason is required." });
        var summary = await _db.Set<ConsultationSummary>().SingleOrDefaultAsync(item =>
            item.Id == id && item.DoctorId == CurrentUserId, cancellationToken);
        if (summary is null) return NotFound(new { error = "Consultation summary not found." });
        if (summary.Status != ConsultationSummaryStatus.Published)
            return Conflict(new { error = "Only a published summary can be amended." });
        summary.Version++;
        summary.LastAmendmentReason = request.AmendmentReason.Trim();
        ApplySummary(summary, request.Summary, request.Diagnosis, request.CareInstructions,
            request.WarningSigns, request.PatientMessage, DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "AmendConsultationSummary", nameof(ConsultationSummary), summary.Id.ToString(),
            CurrentUserId, $"Doctor amended published consultation summary to version {summary.Version}");
        await NotifyPatientAsync(
            summary.PatientId, PatientNotificationCategory.Appointment,
            "Visit summary updated", "Your doctor has updated a previously published visit summary.",
            $"piya://health/summaries/{summary.Id}",
            $"summary:{summary.Id}:version:{summary.Version}", cancellationToken);
        return Ok(await BuildSummaryResponseAsync(summary, cancellationToken));
    }

    [HttpPost("summaries/{summaryId:guid}/follow-up")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<FollowUpPlanResponse>> CreateFollowUp(
        Guid summaryId,
        [FromBody] CreateFollowUpPlanRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
            return BadRequest(new { error = "A concise follow-up reason is required." });
        if (request.EarliestAt <= DateTime.UtcNow || request.LatestAt < request.EarliestAt ||
            request.LatestAt > DateTime.UtcNow.AddYears(1))
            return BadRequest(new { error = "Provide a valid future follow-up window of at most one year." });
        if (request.DurationMinutes is < 10 or > 240)
            return BadRequest(new { error = "Follow-up duration must be between 10 and 240 minutes." });
        var summary = await _db.Set<ConsultationSummary>().AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == summaryId && item.DoctorId == CurrentUserId &&
            item.Status == ConsultationSummaryStatus.Published, cancellationToken);
        if (summary is null) return NotFound(new { error = "Published consultation summary not found." });
        var source = await _db.Set<Appointment>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == summary.AppointmentId, cancellationToken);
        if (source is null) return NotFound(new { error = "Source appointment not found." });
        var duplicate = await _db.Set<FollowUpPlan>().AsNoTracking().AnyAsync(item =>
            item.ConsultationSummaryId == summary.Id &&
            (item.Status == FollowUpStatus.Recommended || item.Status == FollowUpStatus.Scheduled),
            cancellationToken);
        if (duplicate) return Conflict(new { error = "An active follow-up already exists for this consultation." });

        var now = DateTime.UtcNow;
        var plan = new FollowUpPlan
        {
            Id = Guid.NewGuid(), ConsultationSummaryId = summary.Id,
            SourceAppointmentId = source.Id, PatientId = source.PatientId,
            DoctorId = source.DoctorId, HospitalId = source.HospitalId,
            Reason = request.Reason.Trim(), EarliestAt = request.EarliestAt,
            LatestAt = request.LatestAt, DurationMinutes = request.DurationMinutes,
            Status = FollowUpStatus.Recommended, CreatedAt = now, UpdatedAt = now
        };
        _db.Set<FollowUpPlan>().Add(plan);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "CreateFollowUpPlan", nameof(FollowUpPlan), plan.Id.ToString(), CurrentUserId,
            "Doctor recommended a patient-scheduled follow-up window");
        await NotifyPatientAsync(
            plan.PatientId, PatientNotificationCategory.Appointment,
            "Follow-up recommended", "Your doctor has recommended a follow-up visit.",
            $"piya://appointments/follow-ups/{plan.Id}", $"follow-up:{plan.Id}:recommended",
            cancellationToken);
        return CreatedAtAction(nameof(GetMyFollowUps), await BuildFollowUpResponseAsync(plan, cancellationToken));
    }

    [HttpGet("follow-ups/mine")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<IReadOnlyList<FollowUpPlanResponse>>> GetMyFollowUps(
        CancellationToken cancellationToken)
    {
        await MaterializeFollowUpStatusAsync(CurrentUserId, cancellationToken);
        var plans = await _db.Set<FollowUpPlan>().AsNoTracking()
            .Where(item => item.PatientId == CurrentUserId)
            .OrderByDescending(item => item.CreatedAt).Take(100)
            .ToListAsync(cancellationToken);
        return Ok(await BuildFollowUpResponsesAsync(plans, cancellationToken));
    }

    [HttpPost("follow-ups/{id:guid}/schedule")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<FollowUpPlanResponse>> ScheduleFollowUp(
        Guid id,
        [FromBody] ScheduleFollowUpRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await _db.Set<FollowUpPlan>().SingleOrDefaultAsync(item =>
            item.Id == id && item.PatientId == CurrentUserId, cancellationToken);
        if (plan is null) return NotFound(new { error = "Follow-up plan not found." });
        if (plan.Status != FollowUpStatus.Recommended)
            return Conflict(new { error = "Only a recommended follow-up can be scheduled." });
        if (request.ScheduledAt < plan.EarliestAt || request.ScheduledAt > plan.LatestAt ||
            request.ScheduledAt <= DateTime.UtcNow)
            return BadRequest(new { error = "Choose a future time inside the recommended follow-up window." });

        Appointment appointment;
        try
        {
            appointment = await _appointmentService.BookAppointmentAsync(new Appointment
            {
                PatientId = plan.PatientId, DoctorId = plan.DoctorId,
                HospitalId = plan.HospitalId, ScheduledAt = request.ScheduledAt,
                DurationMinutes = plan.DurationMinutes,
                Reason = $"Follow-up: {plan.Reason}", Status = AppointmentStatus.Scheduled
            });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }

        plan.Status = FollowUpStatus.Scheduled;
        plan.ScheduledAppointmentId = appointment.Id;
        plan.ScheduledAt = appointment.ScheduledAt;
        plan.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "ScheduleFollowUp", nameof(FollowUpPlan), plan.Id.ToString(), CurrentUserId,
            $"Patient scheduled follow-up appointment {appointment.Id}");
        await NotifyPatientAsync(
            plan.PatientId, PatientNotificationCategory.Appointment,
            "Follow-up scheduled", "Your follow-up appointment has been scheduled.",
            $"piya://appointments/{appointment.Id}", $"follow-up:{plan.Id}:scheduled",
            cancellationToken);
        return Ok(await BuildFollowUpResponseAsync(plan, cancellationToken));
    }

    [HttpPost("follow-ups/{id:guid}/decline")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> DeclineFollowUp(
        Guid id,
        [FromBody] DeclineFollowUpRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Reason?.Length > 500) return BadRequest(new { error = "Reason is too long." });
        var plan = await _db.Set<FollowUpPlan>().SingleOrDefaultAsync(item =>
            item.Id == id && item.PatientId == CurrentUserId, cancellationToken);
        if (plan is null) return NotFound(new { error = "Follow-up plan not found." });
        if (plan.Status != FollowUpStatus.Recommended)
            return Conflict(new { error = "Only a recommended follow-up can be declined." });
        plan.Status = FollowUpStatus.Declined;
        plan.DeclinedAt = DateTime.UtcNow;
        plan.DeclineReason = Clean(request?.Reason);
        plan.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "DeclineFollowUp", nameof(FollowUpPlan), plan.Id.ToString(), CurrentUserId,
            "Patient declined a follow-up recommendation");
        return NoContent();
    }

    [HttpPost("follow-ups/{id:guid}/cancel")]
    public async Task<IActionResult> CancelFollowUp(Guid id, CancellationToken cancellationToken)
    {
        var plan = await _db.Set<FollowUpPlan>().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (plan is null) return NotFound(new { error = "Follow-up plan not found." });
        if (plan.PatientId != CurrentUserId && plan.DoctorId != CurrentUserId &&
            !User.IsInRole("Admin") && !User.IsInRole("SuperAdmin")) return Forbid();
        if (plan.Status is FollowUpStatus.Completed or FollowUpStatus.Cancelled)
            return Conflict(new { error = "This follow-up is already closed." });
        if (plan.Status == FollowUpStatus.Scheduled && plan.ScheduledAppointmentId.HasValue)
            return Conflict(new { error = "Cancel the booked appointment through the appointment workflow." });
        plan.Status = FollowUpStatus.Cancelled;
        plan.CancelledAt = DateTime.UtcNow;
        plan.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "CancelFollowUpPlan", nameof(FollowUpPlan), plan.Id.ToString(), CurrentUserId,
            "Authorized participant cancelled an unscheduled follow-up plan");
        return NoContent();
    }

    private async Task MaterializeFollowUpStatusAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var plans = await _db.Set<FollowUpPlan>().Where(item =>
            item.PatientId == patientId && item.Status == FollowUpStatus.Scheduled &&
            item.ScheduledAppointmentId.HasValue).ToListAsync(cancellationToken);
        if (plans.Count == 0) return;
        var appointmentIds = plans.Select(item => item.ScheduledAppointmentId!.Value).ToArray();
        var appointments = await _db.Set<Appointment>().AsNoTracking()
            .Where(item => appointmentIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var changed = false;
        foreach (var plan in plans)
        {
            if (!appointments.TryGetValue(plan.ScheduledAppointmentId!.Value, out var appointment)) continue;
            if (appointment.Status == AppointmentStatus.Completed)
            {
                plan.Status = FollowUpStatus.Completed;
                plan.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }
            else if (appointment.Status == AppointmentStatus.Cancelled)
            {
                plan.Status = FollowUpStatus.Cancelled;
                plan.CancelledAt = appointment.CancelledAt ?? DateTime.UtcNow;
                plan.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }
        }
        if (changed) await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ConsultationSummaryResponse>> BuildSummaryResponsesAsync(
        IReadOnlyList<ConsultationSummary> summaries,
        CancellationToken cancellationToken)
    {
        if (summaries.Count == 0) return [];
        var appointmentIds = summaries.Select(item => item.AppointmentId).Distinct().ToArray();
        var appointments = await _db.Set<Appointment>().AsNoTracking()
            .Where(item => appointmentIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var doctorIds = summaries.Select(item => item.DoctorId).Distinct().ToArray();
        var doctors = await _db.Set<User>().AsNoTracking()
            .Where(item => doctorIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var hospitalIds = appointments.Values.Select(item => item.HospitalId).Distinct().ToArray();
        var hospitals = await _db.Set<Hospital>().AsNoTracking()
            .Where(item => hospitalIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        return summaries.Select(summary =>
        {
            appointments.TryGetValue(summary.AppointmentId, out var appointment);
            doctors.TryGetValue(summary.DoctorId, out var doctor);
            var hospital = appointment is not null && hospitals.TryGetValue(appointment.HospitalId, out var found)
                ? found : null;
            return ToSummaryResponse(summary, appointment, doctor, hospital);
        }).ToList();
    }

    private async Task<ConsultationSummaryResponse> BuildSummaryResponseAsync(
        ConsultationSummary summary,
        CancellationToken cancellationToken) =>
        (await BuildSummaryResponsesAsync([summary], cancellationToken)).Single();

    private static ConsultationSummaryResponse ToSummaryResponse(
        ConsultationSummary summary,
        Appointment? appointment,
        User? doctor,
        Hospital? hospital) => new(
        summary.Id, summary.AppointmentId, summary.PatientId, summary.DoctorId,
        doctor is null ? "Doctor" : $"Dr. {doctor.FirstName} {doctor.LastName}".Trim(),
        hospital?.Name ?? "Healthcare facility", appointment?.ScheduledAt ?? summary.CreatedAt,
        summary.Status, summary.Summary, summary.Diagnosis, summary.CareInstructions,
        summary.WarningSigns, summary.PatientMessage, summary.LastAmendmentReason, summary.Version,
        summary.PublishedAt, summary.UpdatedAt);

    private async Task<IReadOnlyList<FollowUpPlanResponse>> BuildFollowUpResponsesAsync(
        IReadOnlyList<FollowUpPlan> plans,
        CancellationToken cancellationToken)
    {
        if (plans.Count == 0) return [];
        var doctorIds = plans.Select(item => item.DoctorId).Distinct().ToArray();
        var hospitalIds = plans.Select(item => item.HospitalId).Distinct().ToArray();
        var doctors = await _db.Set<User>().AsNoTracking().Where(item => doctorIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var hospitals = await _db.Set<Hospital>().AsNoTracking().Where(item => hospitalIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        return plans.Select(plan =>
        {
            doctors.TryGetValue(plan.DoctorId, out var doctor);
            hospitals.TryGetValue(plan.HospitalId, out var hospital);
            return ToFollowUpResponse(plan, doctor, hospital);
        }).ToList();
    }

    private async Task<FollowUpPlanResponse> BuildFollowUpResponseAsync(
        FollowUpPlan plan,
        CancellationToken cancellationToken) =>
        (await BuildFollowUpResponsesAsync([plan], cancellationToken)).Single();

    private static FollowUpPlanResponse ToFollowUpResponse(
        FollowUpPlan plan,
        User? doctor,
        Hospital? hospital) => new(
        plan.Id, plan.ConsultationSummaryId, plan.SourceAppointmentId,
        plan.PatientId, plan.DoctorId, plan.HospitalId,
        doctor is null ? "Doctor" : $"Dr. {doctor.FirstName} {doctor.LastName}".Trim(),
        hospital?.Name ?? "Healthcare facility", plan.Reason,
        plan.EarliestAt, plan.LatestAt, plan.DurationMinutes, plan.Status,
        plan.ScheduledAppointmentId, plan.ScheduledAt, plan.DeclineReason, plan.UpdatedAt);

    private static string? ValidateSummary(
        string summary,
        string? diagnosis,
        string? instructions,
        string? warningSigns,
        string? patientMessage)
    {
        if (string.IsNullOrWhiteSpace(summary) || summary.Length > 8000)
            return "A consultation summary of at most 8,000 characters is required.";
        if (diagnosis?.Length > 4000 || instructions?.Length > 8000 ||
            warningSigns?.Length > 4000 || patientMessage?.Length > 4000)
            return "One or more consultation summary fields are too long.";
        return null;
    }

    private static void ApplySummary(
        ConsultationSummary target,
        string summary,
        string? diagnosis,
        string? instructions,
        string? warningSigns,
        string? patientMessage,
        DateTime now)
    {
        target.Summary = summary.Trim();
        target.Diagnosis = Clean(diagnosis);
        target.CareInstructions = Clean(instructions);
        target.WarningSigns = Clean(warningSigns);
        target.PatientMessage = Clean(patientMessage);
        target.UpdatedAt = now;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task NotifyPatientAsync(
        Guid patientId,
        PatientNotificationCategory category,
        string title,
        string body,
        string route,
        string dedupeKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _inbox.EnqueueAsync(
                patientId, category, title, body, route,
                new Dictionary<string, string> { ["type"] = "continuity_of_care" },
                dedupeKey, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "Continuity-of-care record saved but inbox notification {DedupeKey} failed", dedupeKey);
        }
    }
}
