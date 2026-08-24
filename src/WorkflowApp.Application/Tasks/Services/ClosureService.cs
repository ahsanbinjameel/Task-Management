using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Notifications;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Services;

public interface IClosureService
{
    /// <summary>
    /// What still stands between this task and closure. Read-only, so a client can grey out the
    /// close button and say why instead of offering it and returning a 409.
    /// </summary>
    Task<Result<ClosureChecklistDto>> EvaluateAsync(long taskId, CancellationToken ct = default);

    /// <summary>
    /// Closes the task once every requirement is satisfied. Accepts a task sitting in either
    /// QCPassed or ReadyForClosure and walks the remaining steps in one commit.
    /// </summary>
    Task<Result<TaskDetailDto>> CloseAsync(
        long taskId, long actingUserId, CloseTaskDto request, CancellationToken ct = default);

    /// <summary>
    /// Puts closed work back in play. Always carries a reason, and resets the QC requirement: a task
    /// that has been reopened needs a fresh QC pass before it can close again.
    /// </summary>
    Task<Result<TaskDetailDto>> ReopenAsync(
        long taskId, long actingUserId, ReopenTaskDto request, CancellationToken ct = default);
}

/// <summary>
/// The last gate. Closure is the one transition nobody should be able to reach by accident, so the
/// preconditions live here as a named, inspectable checklist rather than as scattered guard clauses.
///
/// The list is deliberately about evidence, not ceremony: QC signed off, the criteria it signed off
/// on still hold, somebody wrote down what was done, no timer is still running, and no child work is
/// outstanding. Anything genuinely exceptional goes through the override path on
/// <see cref="ITaskWorkflowService"/>, which demands <c>Task.Override</c> and a reason and is
/// recorded as a forced move.
/// </summary>
public sealed class ClosureService : IClosureService
{
    private static readonly WorkTaskStatus[] TerminalStatuses =
    {
        WorkTaskStatus.Closed, WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate
    };

    private readonly IWorkflowDbContext _db;
    private readonly ITaskQueryService _queries;
    private readonly IActivityLogger _activity;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ClosureService> _logger;

    public ClosureService(
        IWorkflowDbContext db,
        ITaskQueryService queries,
        IActivityLogger activity,
        IAuditService audit,
        INotificationService notifications,
        IDateTimeProvider clock,
        ILogger<ClosureService> logger)
    {
        _db = db;
        _queries = queries;
        _activity = activity;
        _audit = audit;
        _notifications = notifications;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<ClosureChecklistDto>> EvaluateAsync(long taskId, CancellationToken ct = default)
    {
        var task = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<ClosureChecklistDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        var requirements = await RequirementsAsync(task, ct);

        return Result<ClosureChecklistDto>.Success(new ClosureChecklistDto(
            taskId, requirements.All(r => r.IsMet), requirements));
    }

    public async Task<Result<TaskDetailDto>> ReopenAsync(
        long taskId, long actingUserId, ReopenTaskDto request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (task.Status != WorkTaskStatus.Closed)
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "reopen.not_closed",
                $"A task in {TaskWorkflowService.Humanize(task.Status)} is not closed, so it cannot be reopened."));

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<TaskDetailDto>.Failure(Error.Validation(
                "reopen.reason_required", "Reopening closed work requires a reason."));

        TaskStatusJournal.Write(
            _db, _activity, task, WorkTaskStatus.Reopened, actingUserId, now,
            request.Reason, ActivityType.TaskReopened,
            $"{task.TaskNumber} reopened: {request.Reason}");

        _notifications.RaiseFor(
            new[] { task.PrimaryAssigneeUserId }, actingUserId,
            $"{task.TaskNumber} has been reopened",
            request.Reason, NotificationService.LinkTask, task.Id);

        _audit.Record(
            AuditActions.TaskReopened,
            actorUserId: actingUserId,
            entityType: nameof(WorkTask),
            entityId: task.Id,
            previousValues: new { Status = WorkTaskStatus.Closed.ToString() },
            newValues: new { Status = WorkTaskStatus.Reopened.ToString(), request.Reason });

        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Task {TaskNumber} reopened by user {UserId}: {Reason}",
            task.TaskNumber, actingUserId, request.Reason);

        return await _queries.GetAsync(taskId, ct);
    }

    public async Task<Result<TaskDetailDto>> CloseAsync(
        long taskId, long actingUserId, CloseTaskDto request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        // Closing an already-closed task is what a retried request looks like.
        if (task.Status == WorkTaskStatus.Closed)
            return await _queries.GetAsync(taskId, ct);

        if (task.Status is not (WorkTaskStatus.QCPassed or WorkTaskStatus.ReadyForClosure))
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "closure.not_ready_to_close",
                $"A task in {TaskWorkflowService.Humanize(task.Status)} cannot be closed. It must pass QC first."));

        // Applied before the check so the supplied resolution can satisfy the resolution requirement.
        if (!string.IsNullOrWhiteSpace(request.Resolution))
            task.Resolution = request.Resolution;

        var requirements = await RequirementsAsync(task, ct);
        var unmet = requirements.Where(r => !r.IsMet).ToList();

        if (unmet.Count > 0)
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "closure.requirements_unmet",
                "This task does not yet meet the closure requirements: " +
                string.Join(" ", unmet.Select(r => r.Detail ?? r.Description))));

        // QCPassed to ReadyForClosure is a real step in the map, not a formality, so it gets its own
        // history row even when the same call goes straight on to close.
        if (task.Status == WorkTaskStatus.QCPassed)
        {
            TaskStatusJournal.Write(
                _db, _activity, task, WorkTaskStatus.ReadyForClosure, actingUserId, now,
                request.Reason, ActivityType.QCPassed,
                $"{task.TaskNumber} cleared QC and is ready for closure.");
        }

        TaskStatusJournal.Write(
            _db, _activity, task, WorkTaskStatus.Closed, actingUserId, now,
            request.Reason, ActivityType.TaskClosed, $"{task.TaskNumber} closed.");

        // The assignee and whoever asked for the work both want to know it landed.
        var requesterId = task.RequestId is { } requestId
            ? await _db.Requests.AsNoTracking().Where(r => r.Id == requestId)
                .Select(r => (long?)r.RequestedByUserId).FirstOrDefaultAsync(ct)
            : null;

        _notifications.RaiseFor(
            new[] { task.PrimaryAssigneeUserId, requesterId }, actingUserId,
            $"{task.TaskNumber} closed", task.Resolution, NotificationService.LinkTask, task.Id);

        _audit.Record(
            AuditActions.TaskClosed,
            actorUserId: actingUserId,
            entityType: nameof(WorkTask),
            entityId: task.Id,
            newValues: new { task.Resolution, request.Reason });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Task {TaskNumber} closed by user {UserId}", task.TaskNumber, actingUserId);

        return await _queries.GetAsync(taskId, ct);
    }

    // --- the checklist -------------------------------------------------------------------

    private async Task<IReadOnlyList<ClosureRequirementDto>> RequirementsAsync(
        WorkTask task, CancellationToken ct)
    {
        var latestQC = await _db.QCReviews.AsNoTracking()
            .Where(q => q.TaskId == task.Id)
            .OrderByDescending(q => q.AttemptNumber)
            .FirstOrDefaultAsync(ct);

        // A pass recorded before the task was reopened says nothing about the work done since.
        var reopenedAt = await _db.StatusHistories.AsNoTracking()
            .Where(h => h.TaskId == task.Id && h.ToStatus == WorkTaskStatus.Reopened)
            .OrderByDescending(h => h.ChangedAt).ThenByDescending(h => h.Id)
            .Select(h => (DateTimeOffset?)h.ChangedAt)
            .FirstOrDefaultAsync(ct);

        var qcIsCurrent = latestQC is { Result: QCResult.Passed } &&
                          (reopenedAt is null || latestQC.ReviewedAt >= reopenedAt);

        var openSessions = await _db.WorkSessions.AsNoTracking()
            .CountAsync(s => s.TaskId == task.Id && s.Status == WorkSessionStatus.Active, ct);

        // Only required subtasks hold the parent back. One marked optional is a deliberate
        // statement that the parent can finish without it.
        var openSubtasks = await _db.Tasks.AsNoTracking()
            .CountAsync(t => t.ParentTaskId == task.Id && t.IsRequired
                             && !TerminalStatuses.Contains(t.Status), ct);

        var criteria = QCService.MergeVerdicts(task.AcceptanceCriteria, latestQC?.AcceptanceCriteriaResults);
        var unmetCriteria = criteria.Where(c => c.Met != true).Select(c => c.Index + 1).ToList();

        return new List<ClosureRequirementDto>
        {
            new("closure.qc_passed",
                "The most recent QC review passed, after any reopen.",
                qcIsCurrent,
                latestQC is null
                    ? "No QC review has been recorded."
                    : latestQC.Result != QCResult.Passed
                        ? $"The most recent QC attempt ({latestQC.AttemptNumber}) was {latestQC.Result}."
                        : qcIsCurrent
                            ? null
                            : "The task was reopened after it last passed QC, so it needs a fresh review."),

            new("closure.criteria_met",
                "Every acceptance criterion is marked as met.",
                unmetCriteria.Count == 0,
                unmetCriteria.Count == 0
                    ? null
                    : $"Acceptance criteria not met or not evaluated: {string.Join(", ", unmetCriteria)}."),

            new("closure.resolution",
                "A resolution has been recorded.",
                !string.IsNullOrWhiteSpace(task.Resolution),
                string.IsNullOrWhiteSpace(task.Resolution)
                    ? "No resolution has been written for this task."
                    : null),

            new("closure.no_open_sessions",
                "No work session is still running.",
                openSessions == 0,
                openSessions == 0 ? null : $"{openSessions} work session(s) are still running."),

            new("closure.subtasks_closed",
                "Every smaller task that has to be done is finished.",
                openSubtasks == 0,
                openSubtasks == 0
                    ? null
                    : openSubtasks == 1
                        ? "1 smaller task still has to be finished."
                        : $"{openSubtasks} smaller tasks still have to be finished.")
        };
    }
}
