using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Automates timing and completion state for clinician-authored care tasks. It
/// intentionally contains no diagnostic or treatment recommendation engine.
/// </summary>
public sealed class CareLoopService(
    PharmacyApiDbContext db,
    IAuditService auditService) : ICareLoopService
{
    private readonly PharmacyApiDbContext _db = db;
    private readonly IAuditService _auditService = auditService;

    public async Task RefreshPatientAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var workflows = await _db.Set<CareLoopWorkflow>()
            .Where(item => item.PatientId == patientId && item.Status == CareLoopStatus.Active)
            .ToListAsync(cancellationToken);
        if (workflows.Count == 0) return;

        var workflowIds = workflows.Select(item => item.Id).ToArray();
        var tasks = await _db.Set<CareLoopTask>()
            .Where(item => workflowIds.Contains(item.WorkflowId) &&
                           (item.Status == CareLoopTaskStatus.Scheduled ||
                            item.Status == CareLoopTaskStatus.Available))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var task in tasks)
        {
            var next = task.DueAt <= now.AddHours(-24)
                ? CareLoopTaskStatus.Overdue
                : task.DueAt <= now
                    ? CareLoopTaskStatus.Available
                    : CareLoopTaskStatus.Scheduled;
            if (next == task.Status) continue;
            task.Status = next;
            task.UpdatedAt = now;
            changed = true;
        }

        if (changed) await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<CareLoopTask?> CompleteTaskAsync(
        Guid patientId,
        Guid taskId,
        string? patientResponse,
        CancellationToken cancellationToken = default) =>
        FinishTaskAsync(patientId, taskId, patientResponse, skip: false, cancellationToken);

    public Task<CareLoopTask?> SkipTaskAsync(
        Guid patientId,
        Guid taskId,
        string? reason,
        CancellationToken cancellationToken = default) =>
        FinishTaskAsync(patientId, taskId, reason, skip: true, cancellationToken);

    private async Task<CareLoopTask?> FinishTaskAsync(
        Guid patientId,
        Guid taskId,
        string? text,
        bool skip,
        CancellationToken cancellationToken)
    {
        var task = await _db.Set<CareLoopTask>().SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (task is null) return null;
        var workflow = await _db.Set<CareLoopWorkflow>()
            .SingleOrDefaultAsync(item => item.Id == task.WorkflowId && item.PatientId == patientId, cancellationToken);
        if (workflow is null) return null;
        if (workflow.Status != CareLoopStatus.Active)
            throw new InvalidOperationException("Only tasks in an active care plan can be updated.");
        if (task.Status is CareLoopTaskStatus.Completed or CareLoopTaskStatus.Skipped or CareLoopTaskStatus.Cancelled)
            throw new InvalidOperationException("This care task is already finished.");

        var now = DateTime.UtcNow;
        if (skip)
        {
            task.Status = CareLoopTaskStatus.Skipped;
            task.SkipReason = Clean(text);
            task.SkippedAt = now;
        }
        else
        {
            task.Status = CareLoopTaskStatus.Completed;
            task.PatientResponse = Clean(text);
            task.CompletedAt = now;
        }
        task.UpdatedAt = now;

        var hasOpenTasks = await _db.Set<CareLoopTask>().AnyAsync(item =>
            item.WorkflowId == workflow.Id && item.Id != task.Id &&
            item.Status != CareLoopTaskStatus.Completed &&
            item.Status != CareLoopTaskStatus.Skipped &&
            item.Status != CareLoopTaskStatus.Cancelled,
            cancellationToken);
        if (!hasOpenTasks)
        {
            workflow.Status = CareLoopStatus.Completed;
            workflow.CompletedAt = now;
            workflow.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            skip ? "SkipCareLoopTask" : "CompleteCareLoopTask",
            nameof(CareLoopTask), task.Id.ToString(), patientId,
            skip ? "Patient skipped a care-plan task" : "Patient completed a care-plan task");
        return task;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
