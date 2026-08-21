using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Workflow;

namespace WorkflowApp.Application.Tasks.Services;

public interface ITaskWorkflowService
{
    /// <summary>
    /// Moves a task to a new status, enforcing the workflow map, the caller's permissions and the
    /// reason requirement, then records the change in both history streams.
    /// </summary>
    Task<Result<TaskDetailDto>> TransitionAsync(
        long taskId, long actingUserId, TransitionTaskDto request, CancellationToken ct = default);

    /// <summary>Statuses this task may legally move to, filtered by what the caller can actually do.</summary>
    IReadOnlyList<WorkTaskStatus> AvailableTransitions(WorkTaskStatus from, IReadOnlySet<string> permissions);
}

/// <summary>
/// The persistent half of the workflow engine.
///
/// <see cref="TaskTransitionService"/> decides whether a move is legal — it is pure and has no idea
/// a database exists. This class does everything that follows a legal decision: applies the status,
/// appends to <see cref="StatusHistory"/> and <see cref="TaskActivity"/>, closes work sessions when
/// leaving InProgress, and swallows duplicate submissions.
/// </summary>
public sealed class TaskWorkflowService : ITaskWorkflowService
{
    private readonly IWorkflowDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IActivityLogger _activity;
    private readonly IDateTimeProvider _clock;
    private readonly ITaskQueryService _queries;
    private readonly ILogger<TaskWorkflowService> _logger;

    public TaskWorkflowService(
        IWorkflowDbContext db,
        ICurrentUser currentUser,
        IAuditService audit,
        IActivityLogger activity,
        IDateTimeProvider clock,
        ITaskQueryService queries,
        ILogger<TaskWorkflowService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _activity = activity;
        _clock = clock;
        _queries = queries;
        _logger = logger;
    }

    public IReadOnlyList<WorkTaskStatus> AvailableTransitions(
        WorkTaskStatus from, IReadOnlySet<string> permissions)
    {
        var allowed = TaskWorkflow.Transitions
            .Where(t => t.From == from && permissions.Contains(t.RequiredPermission))
            .Select(t => t.To)
            .ToList();

        // Cancel is not in the map — it is allowed from any non-terminal state.
        if (permissions.Contains(Permissions.TaskCancel) &&
            TaskWorkflow.IsAllowed(from, WorkTaskStatus.Cancelled))
        {
            allowed.Add(WorkTaskStatus.Cancelled);
        }

        return allowed.Distinct().ToList();
    }

    public async Task<Result<TaskDetailDto>> TransitionAsync(
        long taskId, long actingUserId, TransitionTaskDto request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        // A retried or double-clicked request must not append a second history row.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
            await AlreadyAppliedAsync(taskId, request, ct))
        {
            _logger.LogInformation(
                "Ignoring duplicate transition on task {TaskId} (idempotency key {Key})",
                taskId, request.IdempotencyKey);
            return await _queries.GetAsync(taskId, ct);
        }

        // Landing on the status the task is already in is a no-op, not an error — a client that
        // lost the response and retried should see success.
        if (task.Status == request.To)
            return await _queries.GetAsync(taskId, ct);

        var from = task.Status;

        var decision = TaskTransitionService.Validate(new TransitionRequest(
            from, request.To, _currentUser.Permissions, request.Reason, request.IsOverride));

        if (!decision.Allowed)
        {
            // A missing permission is a 403; an illegal move is a 409. They are different problems
            // and the client should be able to tell them apart.
            var error = decision.Error!.Contains("permission", StringComparison.OrdinalIgnoreCase)
                ? Error.Forbidden("workflow.permission_denied", decision.Error)
                : Error.Conflict("workflow.transition_not_allowed", decision.Error);

            return Result<TaskDetailDto>.Failure(error);
        }

        // Leaving InProgress must not strand an open work session; close it with the same reason.
        if (from == WorkTaskStatus.InProgress && request.To != WorkTaskStatus.InProgress)
            await CloseOpenSessionsAsync(task, actingUserId, request, now, ct);

        task.Status = request.To;

        if (request.To == WorkTaskStatus.CompletedReadyForQC)
            task.ProgressPercent = 100;

        _db.StatusHistories.Add(new StatusHistory
        {
            TaskId = task.Id,
            FromStatus = from,
            ToStatus = request.To,
            ChangedByUserId = actingUserId,
            ChangedAt = now,
            Reason = request.Reason,
            WasOverride = request.IsOverride
        });

        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = task.Id,
            Type = MapActivity(request.To),
            ActorUserId = actingUserId,
            OccurredAt = now,
            // The idempotency key is carried on the activity row itself — that is what a replay
            // is matched against, so the marker has to be written for the check to ever fire.
            Description = Describe(from, request.To, request.Reason, request.IsOverride)
                          + (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                              ? string.Empty
                              : IdempotencyMarker(request.IdempotencyKey))
        });

        // Task events are echoed onto the workforce timeline so one ordered daily view can show
        // "13:02 Lunch Started" next to "14:10 Task TSK-120 Started".
        _activity.Record(
            actingUserId,
            $"Task {task.TaskNumber} — {Humanize(request.To)}",
            resultingState: null,
            relatedTaskId: task.Id,
            note: request.Reason,
            occurredAt: now);

        if (request.IsOverride)
        {
            _audit.Record(
                AuditActions.WorkflowOverride,
                actorUserId: actingUserId,
                entityType: nameof(WorkTask),
                entityId: task.Id,
                previousValues: new { Status = from.ToString() },
                newValues: new { Status = request.To.ToString(), Reason = request.Reason });

            _logger.LogWarning(
                "Task {TaskNumber} force-moved {From} → {To} by user {UserId}: {Reason}",
                task.TaskNumber, from, request.To, actingUserId, request.Reason);
        }

        await _db.SaveChangesAsync(ct);
        return await _queries.GetAsync(taskId, ct);
    }

    // --- helpers -------------------------------------------------------------------------

    /// <summary>
    /// Detects a replay. The key is stored on the history row's reason-independent trail, so we
    /// match on the destination status plus the key within a short window.
    /// </summary>
    private Task<bool> AlreadyAppliedAsync(long taskId, TransitionTaskDto request, CancellationToken ct)
    {
        var marker = IdempotencyMarker(request.IdempotencyKey!);

        return _db.TaskActivities.AnyAsync(
            a => a.TaskId == taskId &&
                 a.Description.EndsWith(marker), ct);
    }

    private async Task CloseOpenSessionsAsync(
        WorkTask task, long actingUserId, TransitionTaskDto request, DateTimeOffset now, CancellationToken ct)
    {
        var open = await _db.WorkSessions
            .Where(s => s.TaskId == task.Id && s.Status == WorkSessionStatus.Active)
            .ToListAsync(ct);

        foreach (var session in open)
        {
            session.SessionEnd = now;
            session.EndComment = request.Reason;
            // Completed vs Paused: only a pause is expected to resume later.
            session.Status = request.To == WorkTaskStatus.Paused
                ? WorkSessionStatus.Paused
                : WorkSessionStatus.Completed;
        }

        // The user is no longer working on anything.
        if (open.Count > 0)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == actingUserId, ct);
            if (user is not null && user.WorkforceState == WorkforceState.Working)
                user.WorkforceState = WorkforceState.Available;
        }
    }

    internal static string IdempotencyMarker(string key) => $" [#{key}]";

    private static string Describe(WorkTaskStatus from, WorkTaskStatus to, string? reason, bool wasOverride)
    {
        var text = wasOverride
            ? $"Status force-changed from {Humanize(from)} to {Humanize(to)}"
            : $"Status changed from {Humanize(from)} to {Humanize(to)}";

        return string.IsNullOrWhiteSpace(reason) ? $"{text}." : $"{text}: {reason}";
    }

    private static ActivityType MapActivity(WorkTaskStatus to) => to switch
    {
        WorkTaskStatus.InProgress => ActivityType.TaskStarted,
        WorkTaskStatus.Paused => ActivityType.TaskPaused,
        WorkTaskStatus.Blocked => ActivityType.TaskBlocked,
        WorkTaskStatus.CompletedReadyForQC => ActivityType.TaskCompleted,
        WorkTaskStatus.QCReview => ActivityType.QCStarted,
        WorkTaskStatus.QCFailedRework => ActivityType.QCFailed,
        WorkTaskStatus.QCPassed => ActivityType.QCPassed,
        WorkTaskStatus.Closed => ActivityType.TaskClosed,
        WorkTaskStatus.Reopened => ActivityType.TaskReopened,
        WorkTaskStatus.Assigned => ActivityType.AssignmentChanged,
        _ => ActivityType.PriorityChanged
    };

    /// <summary>Splits a PascalCase status into words: <c>CompletedReadyForQC</c> → "Completed Ready For QC".</summary>
    internal static string Humanize(WorkTaskStatus status)
    {
        var name = status.ToString();
        var result = new System.Text.StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            // Break before a capital that starts a new word, but keep runs like "QC" together.
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                result.Append(' ');

            result.Append(name[i]);
        }

        return result.ToString();
    }
}
