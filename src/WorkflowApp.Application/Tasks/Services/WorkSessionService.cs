using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Notifications;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Services;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Entities.Requests;
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
    /// <summary>Statuses that mean a subtask is finished with, one way or another.</summary>
    private static readonly WorkTaskStatus[] TerminalStatuses =
    {
        WorkTaskStatus.Closed, WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate
    };

    private readonly IWorkflowDbContext _db;
    private readonly ITaskQueryService _queries;
    private readonly ITaskDependencyService _dependencies;
    private readonly IActivityLogger _activity;
    private readonly INotificationService _notifications;
    private readonly IPermissionService _permissions;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<WorkSessionService> _logger;

    public WorkSessionService(
        IWorkflowDbContext db,
        ITaskQueryService queries,
        ITaskDependencyService dependencies,
        IActivityLogger activity,
        INotificationService notifications,
        IPermissionService permissions,
        IDateTimeProvider clock,
        ILogger<WorkSessionService> logger)
    {
        _db = db;
        _queries = queries;
        _dependencies = dependencies;
        _activity = activity;
        _notifications = notifications;
        _permissions = permissions;
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

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        // Time cannot be recorded against a day the employee is not on — but only people whose
        // attendance is actually measured have such a day at all.
        // Task.Work and Workforce.TrackShift are independent by design (see
        // Permissions.WorkforceTrackShift), so demanding an open shift from everyone made one of
        // those combinations a dead end: the timer refused to start, the message said to start a
        // shift, and the only control that could do it is hidden from exactly those people —
        // deliberately, because StartShiftAsync would refuse them too. Asked of the user rather
        // than of the caller's token, for the same reason ShiftService asks it that way.
        if (await _permissions.HasPermissionAsync(userId, Permissions.WorkforceTrackShift, ct)
            && !await _db.ShiftSessions.AnyAsync(s => s.UserId == userId && s.ShiftEnd == null, ct))
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

        // A declared dependency that is not finished is a real reason not to start, not just a
        // badge in the UI. Blocking it here is what makes the graph worth maintaining.
        var blockers = await _dependencies.BlockersAsync(taskId, ct);
        if (blockers.Count > 0)
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "task.blocked_by_dependency",
                $"{task.TaskNumber} is waiting on {string.Join(", ", blockers)}."));

        // ReadyToStart is a scheduling step, not a separate user action — bridge it here so the
        // assignee does not have to press two buttons. It is still recorded: every status change
        // appears in the trail, including the automatic ones.
        if (task.Status == WorkTaskStatus.Assigned)
            BridgeToReadyToStart(task, userId, now);

        if (!TaskWorkflow.IsAllowed(task.Status, WorkTaskStatus.InProgress))
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "workflow.transition_not_allowed",
                $"This cannot be started while it is \"{StatusLabels.For(task.Status)}\"."));

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
                $"This cannot be finished while it is \"{StatusLabels.For(task.Status)}\"."));

        // A parent is not finished while the work it was broken into is still outstanding.
        // Enforced here rather than only by hiding the button: the endpoint is reachable directly,
        // and a stale page would otherwise let a parent through after a subtask reopened.
        var outstanding = await _db.Tasks.AsNoTracking()
            .Where(t => t.ParentTaskId == taskId
                        && t.IsRequired
                        && !TerminalStatuses.Contains(t.Status)
                        && t.Status != WorkTaskStatus.Closed)
            .Select(t => t.TaskNumber)
            .ToListAsync(ct);

        if (outstanding.Count > 0)
        {
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "task.required_subtasks_open",
                outstanding.Count == 1
                    ? $"This cannot be finished yet: {outstanding[0]} still has to be done first."
                    : $"This cannot be finished yet because {outstanding.Count} smaller tasks "
                      + $"still have to be done first ({string.Join(", ", outstanding)})."));
        }

        await CloseActiveSessionAsync(task, userId, null, resolution, WorkSessionStatus.Completed, now, ct);

        if (!string.IsNullOrWhiteSpace(resolution)) task.Resolution = resolution;
        task.ProgressPercent = 100;

        // Completed means "ready for QC", never "closed". Only QC can close it.
        MoveTo(task, WorkTaskStatus.CompletedReadyForQC, userId, now, resolution,
            ActivityType.TaskCompleted, $"Work completed on {task.TaskNumber}; ready for QC.");

        await ReleaseWorkingStateAsync(userId, ct);


        // Finished work sitting unchecked is the most common place for a task to stall.
        await _notifications.RaiseForPermissionAsync(
            Permissions.TaskQCReview, userId,
            $"{task.TaskNumber} is ready to be checked",
            task.Title, NotificationService.LinkTask, task.Id, ct);

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
                $"This cannot be started while it is \"{StatusLabels.For(urgent.Status)}\"."));

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

        // Both pause and block are reason-required transitions in the workflow map.
        var reasonCheck = await ValidateReasonAsync(request, ct);
        if (reasonCheck is not null)
            return Result<TaskDetailDto>.Failure(reasonCheck);

        var reason = request.PauseReasonId is { } id
            ? await _db.PauseReasons.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            : null;

        // The reason decides what happens to the TASK, not which button was pressed. "Waiting for
        // client" means the work genuinely cannot move on, whichever endpoint reported it; "Lunch"
        // never does, because the task is still claimed and continues when the worker gets back.
        // The explicit block endpoint still forces Blocked for the case with no listed reason.
        if (reason is not null)
            target = reason.IsBlocker ? WorkTaskStatus.Blocked : WorkTaskStatus.Paused;

        if (!TaskWorkflow.IsAllowed(task.Status, target))
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "workflow.transition_not_allowed",
                $"This cannot be moved from \"{StatusLabels.For(task.Status)}\" to \"{StatusLabels.For(target)}\"."));

        await CloseActiveSessionAsync(
            task, userId, request.PauseReasonId, request.Comment, WorkSessionStatus.Paused, now, ct);

        var reasonText = await DescribeReasonAsync(request, ct);

        MoveTo(task, target, userId, now, reasonText,
            target == WorkTaskStatus.Blocked ? ActivityType.TaskBlocked : ActivityType.TaskPaused,
            $"{(target == WorkTaskStatus.Blocked ? "Cannot continue" : "Paused")}: {reasonText}");

        // ...and separately, where the PERSON went. Only break/lunch/meeting move them; every other
        // reason leaves them on shift and free to pick up something else.
        await ApplyWorkerStateAsync(userId, reason, request.Comment, task.Id, now, ct);

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

    /// <summary>
    /// Moves the worker's availability, if the reason says they actually went somewhere.
    ///
    /// This is the half of "pause" that is about the person rather than the work. It records an
    /// activity event so the day's timeline and the daily report show the break for what it is —
    /// without one, the time would silently read as productive.
    ///
    /// The workforce state machine still governs the move: if it is not a legal transition the
    /// worker is simply released to Available rather than forced somewhere the machine forbids.
    /// </summary>
    private async Task ApplyWorkerStateAsync(
        long userId, PauseReason? reason, string? details, long taskId,
        DateTimeOffset now, CancellationToken ct)
    {
        var away = reason?.AwayState;

        if (away is null)
        {
            await ReleaseWorkingStateAsync(userId, ct);
            return;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        var transition = WorkforceStateMachine.Find(user.WorkforceState, away.Value);
        if (transition is null)
        {
            await ReleaseWorkingStateAsync(userId, ct);
            return;
        }

        var openShift = await _db.ShiftSessions
            .Where(sh => sh.UserId == userId && sh.ShiftEnd == null)
            .Select(sh => (long?)sh.Id)
            .FirstOrDefaultAsync(ct);

        user.WorkforceState = away.Value;

        _activity.Record(
            userId,
            transition.Label,
            resultingState: away.Value,
            shiftSessionId: openShift,
            relatedTaskId: taskId,
            note: details,
            occurredAt: now);
    }

    /// <summary>Applies a status change and appends to every history stream.</summary>
    private void MoveTo(
        WorkTask task, WorkTaskStatus to, long userId, DateTimeOffset now,
        string? reason, ActivityType activityType, string description) =>
        TaskStatusJournal.Write(_db, _activity, task, to, userId, now, reason, activityType, description);
}
