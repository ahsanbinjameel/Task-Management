using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Workflow;

namespace WorkflowApp.Application.Tasks.Services;

/// <summary>Why work stopped. A blocking reason moves the task to Blocked rather than Paused.</summary>
public sealed record StopWorkDto
{
    public long? PauseReasonId { get; init; }
    public string? Comment { get; init; }
}

public sealed record InterruptDto
{
    /// <summary>The urgent task to switch to.</summary>
    public long TaskId { get; init; }

    public long? PauseReasonId { get; init; }
    public string? Comment { get; init; }
}

public interface IWorkSessionService
{
    Task<Result<TaskDetailDto>> StartAsync(long taskId, long userId, CancellationToken ct = default);
    Task<Result<TaskDetailDto>> PauseAsync(long taskId, long userId, StopWorkDto request, CancellationToken ct = default);
    Task<Result<TaskDetailDto>> BlockAsync(long taskId, long userId, StopWorkDto request, CancellationToken ct = default);
    Task<Result<TaskDetailDto>> CompleteAsync(long taskId, long userId, string? resolution, CancellationToken ct = default);

    /// <summary>
    /// Emergency switch: pause the running task and start an urgent one in a single operation.
    /// </summary>
    Task<Result<TaskDetailDto>> InterruptAsync(long userId, InterruptDto request, CancellationToken ct = default);

    /// <summary>The user's currently running session, if any.</summary>
    Task<WorkSessionDto?> ActiveSessionAsync(long userId, CancellationToken ct = default);
}

/// <summary>
/// The task timer.
///
/// Every start or resume opens a session; every pause, block or completion closes one. Total time
/// is the sum of the closed sessions, so an interrupted afternoon produces an accurate total rather
/// than one misleading start-to-finish span.
///
/// The rule that shapes everything here: <b>one active session per user</b>. It is enforced three
/// ways — a check before opening, a close-then-open sequence in the interruption flow, and the
/// filtered unique index <c>UX_WorkSession_OneActivePerUser</c> as the final backstop.
/// </summary>
public sealed class WorkSessionService : IWorkSessionService
{
    private readonly IWorkflowDbContext _db;
    private readonly ITaskQueryService _queries;
    private readonly IActivityLogger _activity;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<WorkSessionService> _logger;

    public WorkSessionService(
        IWorkflowDbContext db,
        ITaskQueryService queries,
        IActivityLogger activity,
        IDateTimeProvider clock,
        ILogger<WorkSessionService> logger)
    {
        _db = db;
        _queries = queries;
        _activity = activity;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<TaskDetailDto>> StartAsync(long taskId, long userId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (task.PrimaryAssigneeUserId != userId)
            return Result<TaskDetailDto>.Failure(Error.Forbidden(
                "task.not_assignee", "Only the assignee can start work on this task."));

        // Time cannot be recorded against a day the employee is not on.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        if (!await _db.ShiftSessions.AnyAsync(s => s.UserId == userId && s.ShiftEnd == null, ct))
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "shift.not_open", "Start your shift before working on a task."));

        var existing = await _db.WorkSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == WorkSessionStatus.Active, ct);

        if (existing is not null)
        {
            // Already running this very task — treat a repeat click as success, not an error.
            if (existing.TaskId == taskId)
                return await _queries.GetAsync(taskId, ct);

            var otherNumber = await _db.Tasks.Where(t => t.Id == existing.TaskId)
                .Select(t => t.TaskNumber).FirstOrDefaultAsync(ct);

            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "worksession.already_active",
                $"You are already working on {otherNumber}. Pause it first, or use the interrupt action."));
        }

        // ReadyToStart is a scheduling step, not a separate user action — bridge it here so the
        // assignee does not have to press two buttons. It is still recorded: every status change
        // appears in the trail, including the automatic ones.
        if (task.Status == WorkTaskStatus.Assigned)
            BridgeToReadyToStart(task, userId, now);

        if (!TaskWorkflow.IsAllowed(task.Status, WorkTaskStatus.InProgress))
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "workflow.transition_not_allowed",
                $"A task in {TaskWorkflowService.Humanize(task.Status)} cannot be started."));

        OpenSession(task, userId, now);
        MoveTo(task, WorkTaskStatus.InProgress, userId, now, reason: null, ActivityType.TaskStarted,
            $"Work started on {task.TaskNumber}.");

        // Working is a consequence of starting a task — this is the only place it is set.
        if (WorkforceStateMachine.IsAllowed(user.WorkforceState, WorkforceState.Working))
            user.WorkforceState = WorkforceState.Working;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} started work on task {TaskNumber}", userId, task.TaskNumber);
        return await _queries.GetAsync(taskId, ct);
    }

    public Task<Result<TaskDetailDto>> PauseAsync(
        long taskId, long userId, StopWorkDto request, CancellationToken ct = default) =>
        StopAsync(taskId, userId, request, WorkTaskStatus.Paused, ct);

    public Task<Result<TaskDetailDto>> BlockAsync(
        long taskId, long userId, StopWorkDto request, CancellationToken ct = default) =>
        StopAsync(taskId, userId, request, WorkTaskStatus.Blocked, ct);

    public async Task<Result<TaskDetailDto>> CompleteAsync(
        long taskId, long userId, string? resolution, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (task.PrimaryAssigneeUserId != userId)
            return Result<TaskDetailDto>.Failure(Error.Forbidden(
                "task.not_assignee", "Only the assignee can complete this task."));

        if (!TaskWorkflow.IsAllowed(task.Status, WorkTaskStatus.CompletedReadyForQC))
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "workflow.transition_not_allowed",
                $"A task in {TaskWorkflowService.Humanize(task.Status)} cannot be completed."));

        await CloseActiveSessionAsync(task, userId, null, resolution, WorkSessionStatus.Completed, now, ct);

        if (!string.IsNullOrWhiteSpace(resolution)) task.Resolution = resolution;
        task.ProgressPercent = 100;

        // Completed means "ready for QC", never "closed". Only QC can close it.
        MoveTo(task, WorkTaskStatus.CompletedReadyForQC, userId, now, resolution,
            ActivityType.TaskCompleted, $"Work completed on {task.TaskNumber}; ready for QC.");

        await ReleaseWorkingStateAsync(userId, ct);

        await _db.SaveChangesAsync(ct);
        return await _queries.GetAsync(taskId, ct);
    }

    public async Task<Result<TaskDetailDto>> InterruptAsync(
        long userId, InterruptDto request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var urgent = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == request.TaskId, ct);
        if (urgent is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (urgent.PrimaryAssigneeUserId != userId)
            return Result<TaskDetailDto>.Failure(Error.Forbidden(
                "task.not_assignee", "Only the assignee can start work on this task."));

        var active = await _db.WorkSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == WorkSessionStatus.Active, ct);

        if (active is null)
            return await StartAsync(request.TaskId, userId, ct);   // nothing to interrupt

        if (active.TaskId == request.TaskId)
            return await _queries.GetAsync(request.TaskId, ct);

        var interrupted = await _db.Tasks.FirstAsync(t => t.Id == active.TaskId, ct);

        // The interrupted task's session is preserved and paused, never discarded — its recorded
        // time must survive the switch, and it must be resumable afterwards.
        active.SessionEnd = now;
        active.Status = WorkSessionStatus.Paused;
        active.EndPauseReasonId = request.PauseReasonId;
        active.EndComment = request.Comment;
        active.EndedByInterruption = true;
        active.InterruptedByTaskId = request.TaskId;

        MoveTo(interrupted, WorkTaskStatus.Paused, userId, now,
            request.Comment ?? $"Interrupted by {urgent.TaskNumber}",
            ActivityType.TaskInterrupted,
            $"Paused — interrupted by {urgent.TaskNumber}.");

        if (urgent.Status == WorkTaskStatus.Assigned)
            BridgeToReadyToStart(urgent, userId, now);

        if (!TaskWorkflow.IsAllowed(urgent.Status, WorkTaskStatus.InProgress))
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "workflow.transition_not_allowed",
                $"A task in {TaskWorkflowService.Humanize(urgent.Status)} cannot be started."));

        OpenSession(urgent, userId, now);
        MoveTo(urgent, WorkTaskStatus.InProgress, userId, now,
            $"Emergency start, interrupting {interrupted.TaskNumber}",
            ActivityType.TaskStarted,
            $"Work started on {urgent.TaskNumber}, interrupting {interrupted.TaskNumber}.");

        // The close and the open commit together: the single-active-session rule is never violated,
        // not even briefly.
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "User {UserId} interrupted {Interrupted} to work on {Urgent}",
            userId, interrupted.TaskNumber, urgent.TaskNumber);

        return await _queries.GetAsync(request.TaskId, ct);
    }

    public async Task<WorkSessionDto?> ActiveSessionAsync(long userId, CancellationToken ct = default)
    {
        var session = await _db.WorkSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == WorkSessionStatus.Active, ct);

        if (session is null) return null;

        var reasons = await _db.PauseReasons.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return TaskQueryService.ToDto(session, reasons);
    }

    // --- shared machinery ----------------------------------------------------------------

    private async Task<Result<TaskDetailDto>> StopAsync(
        long taskId, long userId, StopWorkDto request, WorkTaskStatus target, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (task.PrimaryAssigneeUserId != userId)
            return Result<TaskDetailDto>.Failure(Error.Forbidden(
                "task.not_assignee", "Only the assignee can change work state on this task."));

        if (!TaskWorkflow.IsAllowed(task.Status, target))
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "workflow.transition_not_allowed",
                $"A task in {TaskWorkflowService.Humanize(task.Status)} cannot move to {TaskWorkflowService.Humanize(target)}."));

        // Both pause and block are reason-required transitions in the workflow map.
        var reasonCheck = await ValidateReasonAsync(request, ct);
        if (reasonCheck is not null)
            return Result<TaskDetailDto>.Failure(reasonCheck);

        await CloseActiveSessionAsync(
            task, userId, request.PauseReasonId, request.Comment, WorkSessionStatus.Paused, now, ct);

        var reasonText = await DescribeReasonAsync(request, ct);

        MoveTo(task, target, userId, now, reasonText,
            target == WorkTaskStatus.Blocked ? ActivityType.TaskBlocked : ActivityType.TaskPaused,
            $"{(target == WorkTaskStatus.Blocked ? "Blocked" : "Paused")}: {reasonText}");

        await ReleaseWorkingStateAsync(userId, ct);

        await _db.SaveChangesAsync(ct);
        return await _queries.GetAsync(taskId, ct);
    }

    /// <summary>
    /// A pause reason is required, and some reasons additionally require a comment — "Waiting for
    /// client" is only useful on a report if it says which client and what for.
    /// </summary>
    private async Task<Error?> ValidateReasonAsync(StopWorkDto request, CancellationToken ct)
    {
        if (request.PauseReasonId is not { } reasonId)
        {
            return string.IsNullOrWhiteSpace(request.Comment)
                ? Error.Validation("worksession.reason_required", "Select a reason, or write a comment.")
                : null;
        }

        var reason = await _db.PauseReasons.FirstOrDefaultAsync(p => p.Id == reasonId, ct);
        if (reason is null)
            return Error.NotFound("pausereason.not_found", "That pause reason does not exist.");

        if (reason.RequiresComment && string.IsNullOrWhiteSpace(request.Comment))
            return Error.Validation("worksession.comment_required", $"\"{reason.Name}\" requires a comment.");

        return null;
    }

    private async Task<string> DescribeReasonAsync(StopWorkDto request, CancellationToken ct)
    {
        var name = request.PauseReasonId is { } id
            ? await _db.PauseReasons.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync(ct)
            : null;

        return (name, request.Comment) switch
        {
            (not null, { Length: > 0 }) => $"{name} — {request.Comment}",
            (not null, _) => name,
            _ => request.Comment ?? "(no reason given)"
        };
    }

    /// <summary>
    /// Applies the automatic Assigned → ReadyToStart step and records it, so the status trail has
    /// no unexplained jumps.
    /// </summary>
    private void BridgeToReadyToStart(WorkTask task, long userId, DateTimeOffset now)
    {
        _db.StatusHistories.Add(new StatusHistory
        {
            TaskId = task.Id,
            FromStatus = task.Status,
            ToStatus = WorkTaskStatus.ReadyToStart,
            ChangedByUserId = userId,
            ChangedAt = now,
            Reason = "Ready to start (automatic on first start)"
        });

        task.Status = WorkTaskStatus.ReadyToStart;
    }

    private void OpenSession(WorkTask task, long userId, DateTimeOffset now) =>
        _db.WorkSessions.Add(new WorkSession
        {
            TaskId = task.Id,
            UserId = userId,
            SessionStart = now,
            Status = WorkSessionStatus.Active
        });

    private async Task CloseActiveSessionAsync(
        WorkTask task, long userId, long? pauseReasonId, string? comment,
        WorkSessionStatus endStatus, DateTimeOffset now, CancellationToken ct)
    {
        var active = await _db.WorkSessions
            .FirstOrDefaultAsync(s => s.TaskId == task.Id && s.UserId == userId
                                      && s.Status == WorkSessionStatus.Active, ct);

        if (active is null) return;

        active.SessionEnd = now;
        active.Status = endStatus;
        active.EndPauseReasonId = pauseReasonId;
        active.EndComment = comment;
    }

    /// <summary>Stopping work returns the user to Available — they are on shift, just not on a task.</summary>
    private async Task ReleaseWorkingStateAsync(long userId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is not null && user.WorkforceState == WorkforceState.Working)
            user.WorkforceState = WorkforceState.Available;
    }

    /// <summary>Applies a status change and appends to both history streams.</summary>
    private void MoveTo(
        WorkTask task, WorkTaskStatus to, long userId, DateTimeOffset now,
        string? reason, ActivityType activityType, string description)
    {
        var from = task.Status;
        task.Status = to;

        _db.StatusHistories.Add(new StatusHistory
        {
            TaskId = task.Id,
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = userId,
            ChangedAt = now,
            Reason = reason
        });

        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = task.Id,
            Type = activityType,
            ActorUserId = userId,
            OccurredAt = now,
            Description = description
        });

        // Echo onto the workforce timeline so the daily view is complete.
        _activity.Record(userId, $"Task {task.TaskNumber} — {TaskWorkflowService.Humanize(to)}",
            resultingState: null, relatedTaskId: task.Id, note: reason, occurredAt: now);
    }
}
