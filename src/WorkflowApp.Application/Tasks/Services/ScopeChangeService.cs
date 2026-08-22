using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Services;

public interface IScopeChangeService
{
    /// <summary>Records that the work has changed shape. Does not alter the task until approved.</summary>
    Task<Result<ScopeChangeDto>> RequestAsync(
        long taskId, long requestedByUserId, RequestScopeChangeDto request, CancellationToken ct = default);

    /// <summary>Accepts the change and applies its estimate and deadline impact to the task.</summary>
    Task<Result<ScopeChangeDto>> ApproveAsync(
        long scopeChangeId, long approvedByUserId, CancellationToken ct = default);

    Task<IReadOnlyList<ScopeChangeDto>> ListAsync(long taskId, CancellationToken ct = default);
}

/// <summary>
/// Scope changes exist so that a task which took three times its estimate can be read correctly
/// afterwards. Without them, a bad estimate and a job that quietly doubled in size look identical in
/// every report.
///
/// So the record is made when the change is <b>requested</b>, and the task's own numbers only move
/// when somebody with approval rights accepts it. The original estimate is never overwritten in
/// place without a row explaining the difference.
/// </summary>
public sealed class ScopeChangeService : IScopeChangeService
{
    private readonly IWorkflowDbContext _db;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;

    public ScopeChangeService(IWorkflowDbContext db, IAuditService audit, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<ScopeChangeDto>> RequestAsync(
        long taskId, long requestedByUserId, RequestScopeChangeDto request, CancellationToken ct = default)
    {
        var task = await _db.Tasks.AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => new { t.Id, t.TaskNumber, t.Status })
            .FirstOrDefaultAsync(ct);

        if (task is null)
            return Result<ScopeChangeDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (task.Status is WorkTaskStatus.Closed or WorkTaskStatus.Cancelled or WorkTaskStatus.Duplicate)
            return Result<ScopeChangeDto>.Failure(Error.Conflict(
                "scope.task_finished", "The scope of finished work cannot be changed. Reopen it first."));

        if (string.IsNullOrWhiteSpace(request.Description))
            return Result<ScopeChangeDto>.Failure(Error.Validation(
                "scope.description_required", "Describe what is changing."));

        var now = _clock.UtcNow;

        var change = new ScopeChange
        {
            TaskId = taskId,
            RequestedByUserId = requestedByUserId,
            RequestedAt = now,
            Description = request.Description.Trim(),
            Reason = request.Reason,
            EstimatedImpactHours = request.EstimatedImpactHours,
            DeadlineImpact = request.DeadlineImpact
        };

        _db.ScopeChanges.Add(change);

        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = taskId,
            Type = ActivityType.ScopeChanged,
            ActorUserId = requestedByUserId,
            OccurredAt = now,
            Description = $"Scope change requested on {task.TaskNumber}: {change.Description}"
        });

        await _db.SaveChangesAsync(ct);
        return Result<ScopeChangeDto>.Success(await ProjectAsync(change, ct));
    }

    public async Task<Result<ScopeChangeDto>> ApproveAsync(
        long scopeChangeId, long approvedByUserId, CancellationToken ct = default)
    {
        var change = await _db.ScopeChanges.FirstOrDefaultAsync(c => c.Id == scopeChangeId, ct);
        if (change is null)
            return Result<ScopeChangeDto>.Failure(Error.NotFound("scope.not_found", "Scope change not found."));

        // Approving twice would apply the hours twice.
        if (change.ApprovedByUserId is not null)
            return Result<ScopeChangeDto>.Failure(Error.Conflict(
                "scope.already_approved", "That scope change has already been approved."));

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == change.TaskId, ct);
        if (task is null)
            return Result<ScopeChangeDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        var now = _clock.UtcNow;
        var previous = new { task.EstimatedEffortHours, task.DueDate };

        change.ApprovedByUserId = approvedByUserId;
        change.ApprovedAt = now;

        if (change.EstimatedImpactHours is { } hours)
            task.EstimatedEffortHours = Math.Max(0m, (task.EstimatedEffortHours ?? 0m) + hours);

        if (change.DeadlineImpact is { } deadline)
            task.DueDate = deadline;

        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = task.Id,
            Type = ActivityType.ScopeChangeApproved,
            ActorUserId = approvedByUserId,
            OccurredAt = now,
            Description = $"Scope change approved on {task.TaskNumber}. " +
                          $"Estimate {previous.EstimatedEffortHours?.ToString() ?? "none"} → " +
                          $"{task.EstimatedEffortHours?.ToString() ?? "none"}h."
        });

        _audit.Record(
            AuditActions.ScopeChangeApproved,
            actorUserId: approvedByUserId,
            entityType: nameof(WorkTask),
            entityId: task.Id,
            previousValues: previous,
            newValues: new { task.EstimatedEffortHours, task.DueDate, change.Description });

        await _db.SaveChangesAsync(ct);
        return Result<ScopeChangeDto>.Success(await ProjectAsync(change, ct));
    }

    public async Task<IReadOnlyList<ScopeChangeDto>> ListAsync(long taskId, CancellationToken ct = default)
    {
        var changes = await _db.ScopeChanges.AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.RequestedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);

        if (changes.Count == 0) return Array.Empty<ScopeChangeDto>();

        var names = await NamesAsync(changes.Select(c => c.RequestedByUserId), ct);
        return changes.Select(c => ToDto(c, names.GetValueOrDefault(c.RequestedByUserId))).ToList();
    }

    // --- helpers -------------------------------------------------------------------------

    private async Task<ScopeChangeDto> ProjectAsync(ScopeChange change, CancellationToken ct)
    {
        var names = await NamesAsync(new[] { change.RequestedByUserId }, ct);
        return ToDto(change, names.GetValueOrDefault(change.RequestedByUserId));
    }

    private Task<Dictionary<long, string>> NamesAsync(IEnumerable<long> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    private static ScopeChangeDto ToDto(ScopeChange c, string? requestedByName) =>
        new(c.Id, c.TaskId, c.RequestedByUserId, requestedByName, c.RequestedAt,
            c.Description, c.Reason, c.EstimatedImpactHours, c.DeadlineImpact,
            c.ApprovedByUserId, c.ApprovedAt);
}
