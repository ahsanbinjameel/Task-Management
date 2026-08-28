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

    /// <summary>
    /// The requester's "It's fixed" (PRODUCT-CORE §7). Closes the work in their name.
    ///
    /// Only the person who raised the originating request may call it — decided on the record
    /// rather than by a permission, because the answer depends on the task, not on the caller's
    /// authority. A coordinator holding every permission there is still cannot confirm a fix on
    /// somebody else's behalf.
    /// </summary>
    Task<Result<TaskDetailDto>> AcceptAsync(
        long taskId, long actingUserId, AcceptFixDto request, CancellationToken ct = default);

    /// <summary>
    /// The requester's "Still not fixed". Sends the work back with their words attached, and
    /// requires a fresh QC pass before it can reach closure again.
    /// </summary>
    Task<Result<TaskDetailDto>> RejectAsync(
        long taskId, long actingUserId, RejectFixDto request, CancellationToken ct = default);
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
        var requester = await RequesterAsync(task, ct);

        return Result<ClosureChecklistDto>.Success(new ClosureChecklistDto(
            taskId, requirements.All(r => r.IsMet), requirements,
            RequiresRequesterAcceptance: requester is not null,
            RequesterDisplayName: requester?.DisplayName,
            RequesterHasConfirmed: task.Status == WorkTaskStatus.Closed));
    }

    /// <summary>
    /// Who asked for this work, when there is such a person. That is the whole acceptance policy
    /// (PRODUCT-CORE §4.14): work with a requester is confirmed by them; work with none — a task
    /// raised internally, or a subtask — closes on the quality check alone, because there is nobody
    /// to ask and inventing a confirmation step for an empty seat only strands the work.
    ///
    /// One method, so changing the rule is one edit rather than a hunt through the call sites.
    /// </summary>
    private async Task<Requester?> RequesterAsync(WorkTask task, CancellationToken ct)
    {
        if (task.RequestId is not { } requestId) return null;

        return await _db.Requests.AsNoTracking()
            .Where(r => r.Id == requestId)
            .Join(_db.Users.AsNoTracking(), r => r.RequestedByUserId, u => u.Id,
                (r, u) => new Requester(u.Id, u.DisplayName))
            .FirstOrDefaultAsync(ct);
    }

    private sealed record Requester(long UserId, string DisplayName);

    public async Task<Result<TaskDetailDto>> AcceptAsync(
        long taskId, long actingUserId, AcceptFixDto request, CancellationToken ct = default)
    {
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        // Accepting twice is what a double-click looks like.
        if (task.Status == WorkTaskStatus.Closed)
            return await _queries.GetAsync(taskId, ct);

        var guard = await GuardRequesterAsync(task, actingUserId, ct);
        if (guard is not null) return Result<TaskDetailDto>.Failure(guard);

        // Their confirmation *is* the resolution, where nobody wrote one. Said plainly, because it
        // ends up in the closure record and on the report: this closed because the person who
        // asked for it said it was fixed, which is a different fact from "QC passed".
        if (string.IsNullOrWhiteSpace(task.Resolution))
        {
            task.Resolution = string.IsNullOrWhiteSpace(request.Note)
                ? "Confirmed fixed by the requester."
                : $"Confirmed fixed by the requester: {request.Note.Trim()}";
        }

        var closed = await CloseAsync(
            taskId, actingUserId,
            new CloseTaskDto { Reason = request.Note?.Trim() }, ct);

        if (closed.IsSuccess)
        {
            _audit.Record(
                AuditActions.TaskClosed,
                actorUserId: actingUserId,
                entityType: nameof(WorkTask),
                entityId: task.Id,
                newValues: new { Accepted = true, request.Note });

            await _db.SaveChangesAsync(ct);
        }

        return closed;
    }

    public async Task<Result<TaskDetailDto>> RejectAsync(
        long taskId, long actingUserId, RejectFixDto request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<TaskDetailDto>.Failure(Error.Validation(
                "acceptance.reason_required",
                "Say what is still wrong, so the work can be put right without another round of questions."));

        var guard = await GuardRequesterAsync(task, actingUserId, ct);
        if (guard is not null) return Result<TaskDetailDto>.Failure(guard);

        TaskStatusJournal.Write(
            _db, _activity, task, WorkTaskStatus.Reopened, actingUserId, now,
            request.Reason, ActivityType.TaskReopened,
            $"{task.TaskNumber}: the requester reports it is still not fixed — {request.Reason}");

        // Everyone who touched it, not just the assignee: the checker passed work that turned out
        // not to satisfy the person who asked, and that is the most useful thing they can learn.
        _notifications.RaiseFor(
            new[] { task.PrimaryAssigneeUserId, task.QCUserId }, actingUserId,
            $"{task.TaskNumber}: still not fixed",
            request.Reason, NotificationService.LinkTask, task.Id);

        _audit.Record(
            AuditActions.TaskReopened,
            actorUserId: actingUserId,
            entityType: nameof(WorkTask),
            entityId: task.Id,
            previousValues: new { Status = WorkTaskStatus.QCPassed.ToString() },
            newValues: new { Status = WorkTaskStatus.Reopened.ToString(), request.Reason, RejectedByRequester = true });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Task {TaskNumber} rejected by its requester {UserId}: {Reason}",
            task.TaskNumber, actingUserId, request.Reason);

        return await _queries.GetAsync(taskId, ct);
    }

    /// <summary>
    /// The two rules both requester actions share: the work has to be waiting on them, and they
    /// have to be the person who asked for it.
    /// </summary>
    private async Task<Error?> GuardRequesterAsync(WorkTask task, long actingUserId, CancellationToken ct)
    {
        if (task.Status is not (WorkTaskStatus.QCPassed or WorkTaskStatus.ReadyForClosure))
            return Error.Conflict(
                "acceptance.not_awaiting_confirmation",
                $"{task.TaskNumber} is {TaskWorkflowService.Humanize(task.Status)}, so there is nothing to confirm yet.");

        var requester = await RequesterAsync(task, ct);

        if (requester is null)
            return Error.Conflict(
                "acceptance.no_requester",
                "This work was not raised by a request, so there is nobody to confirm it.");

        if (requester.UserId != actingUserId)
            return Error.Forbidden(
                "acceptance.not_requester",
                "Only the person who asked for this work can say whether it is fixed.");

        return null;
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
