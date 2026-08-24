using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common.Events;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Notifications;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Workflow;

namespace WorkflowApp.Application.Tasks.Services;

public interface ITaskAssignmentService
{
    Task<Result<TaskDetailDto>> AssignAsync(
        long taskId, long actingUserId, AssignTaskDto request, CancellationToken ct = default);

    Task<Result<TaskDetailDto>> AddCollaboratorAsync(long taskId, long userId, long actingUserId, CancellationToken ct = default);
    Task<Result<TaskDetailDto>> RemoveCollaboratorAsync(long taskId, long userId, CancellationToken ct = default);
    Task<Result<TaskDetailDto>> SetRolesAsync(long taskId, SetTaskRolesDto request, CancellationToken ct = default);
    Task<Result<TaskDetailDto>> UpdateDetailsAsync(long taskId, long actingUserId, UpdateTaskDetailsDto request, CancellationToken ct = default);

    /// <summary>Rewrites an assignee's queue order. Only their own tasks may appear in the list.</summary>
    Task<Result> ReorderQueueAsync(long userId, IReadOnlyList<long> taskIdsInOrder, CancellationToken ct = default);
}

/// <summary>
/// Assignment and queue management.
///
/// The interesting rule is concurrency: two coordinators can open the same unassigned task and
/// both press Assign. The second one must lose rather than silently overwrite the first, so the
/// client's copy of the row version is checked before the write.
/// </summary>
public sealed class TaskAssignmentService : ITaskAssignmentService
{
    private readonly IWorkflowDbContext _db;
    private readonly ITaskQueryService _queries;
    private readonly INotificationService _notifications;
    private readonly IIntegrationEventQueue _events;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<TaskAssignmentService> _logger;

    public TaskAssignmentService(
        IWorkflowDbContext db,
        ITaskQueryService queries,
        INotificationService notifications,
        IIntegrationEventQueue events,
        IDateTimeProvider clock,
        ILogger<TaskAssignmentService> logger)
    {
        _db = db;
        _queries = queries;
        _notifications = notifications;
        _events = events;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<TaskDetailDto>> AssignAsync(
        long taskId, long actingUserId, AssignTaskDto request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        // Optimistic concurrency: reject a write based on a version of the row the client has not
        // seen. Skipped when the client sent nothing, or on a provider without ROWVERSION.
        if (!string.IsNullOrWhiteSpace(request.RowVersion) && task.RowVersion is { Length: > 0 })
        {
            var current = Convert.ToBase64String(task.RowVersion);
            if (!string.Equals(current, request.RowVersion, StringComparison.Ordinal))
            {
                return Result<TaskDetailDto>.Failure(Error.Conflict(
                    "task.concurrency_conflict",
                    "Someone else changed this task while you were working on it. Reload and try again."));
            }
        }

        if (request.AssigneeUserId is { } assigneeId)
        {
            var assignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == assigneeId, ct);
            if (assignee is null)
                return Result<TaskDetailDto>.Failure(Error.NotFound("user.not_found", "Assignee not found."));

            if (!assignee.IsActive)
                return Result<TaskDetailDto>.Failure(Error.Validation(
                    "task.assignee_inactive", "That user account is deactivated."));
        }

        var previousAssignee = task.PrimaryAssigneeUserId;

        // Reassigning someone's work away from them needs a reason on the record.
        if (previousAssignee is not null &&
            previousAssignee != request.AssigneeUserId &&
            string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<TaskDetailDto>.Failure(Error.Validation(
                "task.reassign_reason_required", "A reason is required to reassign an assigned task."));
        }

        if (previousAssignee == request.AssigneeUserId)
            return await _queries.GetAsync(taskId, ct);   // no change; idempotent

        task.PrimaryAssigneeUserId = request.AssigneeUserId;
        var previousStatus = task.Status;

        if (request.AssigneeUserId is { } newAssignee)
        {
            // Nobody is both owner and helper. If this person was helping, that relationship ends
            // here — leaving it would count the same task twice in reports, once as work they own
            // and once as work they supported.
            var supportRow = await _db.TaskCollaborators
                .FirstOrDefaultAsync(c => c.TaskId == taskId && c.UserId == newAssignee, ct);

            if (supportRow is not null)
            {
                _db.TaskCollaborators.Remove(supportRow);

                _db.TaskActivities.Add(new TaskActivity
                {
                    TaskId = taskId,
                    Type = ActivityType.CollaboratorRemoved,
                    ActorUserId = actingUserId,
                    OccurredAt = now,
                    Description = "Was helping with this task; now responsible for it."
                });
            }

            // New work goes to the end of that person's queue rather than jumping the line.
            var lastPosition = await _db.Tasks
                .Where(t => t.PrimaryAssigneeUserId == newAssignee && t.Id != taskId)
                .MaxAsync(t => (int?)t.QueueOrder, ct) ?? 0;

            task.QueueOrder = lastPosition + 1;

            if (TaskWorkflow.IsAllowed(task.Status, WorkTaskStatus.Assigned))
                task.Status = WorkTaskStatus.Assigned;
        }
        else
        {
            // Unassigned work returns to the pool.
            task.QueueOrder = 0;
            if (TaskWorkflow.IsAllowed(task.Status, WorkTaskStatus.ReadyForAssignment))
                task.Status = WorkTaskStatus.ReadyForAssignment;
        }

        var described = await DescribeAssignmentAsync(
            previousAssignee, request.AssigneeUserId, request.Reason, ct);

        // Assigning moves the task's status, so it belongs in the status trail too. Without this
        // the history jumps from ReadyForAssignment straight to InProgress with no explanation.
        if (task.Status != previousStatus)
        {
            _db.StatusHistories.Add(new StatusHistory
            {
                TaskId = taskId,
                FromStatus = previousStatus,
                ToStatus = task.Status,
                ChangedByUserId = actingUserId,
                ChangedAt = now,
                Reason = request.Reason ?? described
            });
        }

        // Append-only: every assignment and reassignment stays on the record.
        _db.AssignmentHistories.Add(new AssignmentHistory
        {
            TaskId = taskId,
            FromUserId = previousAssignee,
            ToUserId = request.AssigneeUserId,
            AssignedByUserId = actingUserId,
            AssignedAt = now,
            Reason = request.Reason
        });

        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = taskId,
            Type = ActivityType.AssignmentChanged,
            ActorUserId = actingUserId,
            OccurredAt = now,
            Description = described
        });

        // Being given work is the one thing nobody should have to discover by refreshing.
        _notifications.RaiseFor(
            new long?[] { request.AssigneeUserId }, actingUserId,
            $"{task.TaskNumber} assigned to you",
            task.Title, NotificationService.LinkTask, task.Id);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row moved between our read and our write — the other coordinator got there first.
            _logger.LogWarning("Concurrent assignment rejected for task {TaskId}", taskId);
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "task.concurrency_conflict",
                "Someone else changed this task while you were working on it. Reload and try again."));
        }

        return await _queries.GetAsync(taskId, ct);
    }

    public async Task<Result<TaskDetailDto>> AddCollaboratorAsync(
        long taskId, long userId, long actingUserId, CancellationToken ct = default)
    {
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (!await _db.Users.AnyAsync(u => u.Id == userId, ct))
            return Result<TaskDetailDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        // The primary assignee keeps accountability; they are not also a supporting collaborator.
        if (task.PrimaryAssigneeUserId == userId)
            return Result<TaskDetailDto>.Failure(Error.Validation(
                "task.assignee_is_not_collaborator",
                "That person is already responsible for this task, so they cannot also be "
                + "listed as helping with it."));

        if (await _db.TaskCollaborators.AnyAsync(c => c.TaskId == taskId && c.UserId == userId, ct))
            return await _queries.GetAsync(taskId, ct);   // already there; idempotent

        _db.TaskCollaborators.Add(new TaskCollaborator
        {
            TaskId = taskId,
            UserId = userId,
            AddedByUserId = actingUserId,
            AddedAt = _clock.UtcNow
        });

        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = taskId,
            Type = ActivityType.CollaboratorAdded,
            ActorUserId = actingUserId,
            OccurredAt = _clock.UtcNow,
            Description = $"User {userId} added as a support person (helping, not responsible)."
        });

        AnnounceTaskChanged(task);

        await _db.SaveChangesAsync(ct);
        return await _queries.GetAsync(taskId, ct);
    }

    public async Task<Result<TaskDetailDto>> RemoveCollaboratorAsync(
        long taskId, long userId, CancellationToken ct = default)
    {
        var link = await _db.TaskCollaborators
            .FirstOrDefaultAsync(c => c.TaskId == taskId && c.UserId == userId, ct);

        if (link is not null)
        {
            _db.TaskCollaborators.Remove(link);

            var task = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
            if (task is not null) AnnounceTaskChanged(task);

            await _db.SaveChangesAsync(ct);
        }

        return await _queries.GetAsync(taskId, ct);
    }

    /// <summary>
    /// Tells anyone watching this task that its people changed. Support-person rows are not the
    /// task row, so nothing else would raise an event for them.
    /// </summary>
    private void AnnounceTaskChanged(Domain.Entities.Tasks.WorkTask task) =>
        _events.Enqueue(new TaskChangedEvent(
            task.Id, task.TaskNumber, task.Status, task.PrimaryAssigneeUserId, ChangeKind.Updated));

    public async Task<Result<TaskDetailDto>> SetRolesAsync(
        long taskId, SetTaskRolesDto request, CancellationToken ct = default)
    {
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        foreach (var userId in new[] { request.ReviewerUserId, request.QCUserId }.Where(x => x.HasValue))
        {
            if (!await _db.Users.AnyAsync(u => u.Id == userId!.Value, ct))
                return Result<TaskDetailDto>.Failure(Error.NotFound("user.not_found", "User not found."));
        }

        // QC by the person who did the work defeats the point of having QC.
        if (request.QCUserId is { } qc && qc == task.PrimaryAssigneeUserId)
            return Result<TaskDetailDto>.Failure(Error.Validation(
                "task.qc_cannot_be_assignee", "QC must be performed by someone other than the assignee."));

        task.ReviewerUserId = request.ReviewerUserId;
        task.QCUserId = request.QCUserId;

        await _db.SaveChangesAsync(ct);
        return await _queries.GetAsync(taskId, ct);
    }

    public async Task<Result<TaskDetailDto>> UpdateDetailsAsync(
        long taskId, long actingUserId, UpdateTaskDetailsDto request, CancellationToken ct = default)
    {
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (request.Priority is { } priority && priority != task.Priority)
        {
            _db.TaskActivities.Add(new TaskActivity
            {
                TaskId = taskId,
                Type = ActivityType.PriorityChanged,
                ActorUserId = actingUserId,
                OccurredAt = _clock.UtcNow,
                Description = $"Priority changed from {task.Priority} to {priority}."
            });

            task.Priority = priority;
        }

        if (request.EstimatedEffortHours.HasValue) task.EstimatedEffortHours = request.EstimatedEffortHours;
        if (request.DueDate.HasValue) task.DueDate = request.DueDate;
        if (request.AcceptanceCriteria is not null) task.AcceptanceCriteria = request.AcceptanceCriteria;
        if (request.Resolution is not null) task.Resolution = request.Resolution;
        if (request.ProgressPercent is { } progress) task.ProgressPercent = Math.Clamp(progress, 0, 100);

        await _db.SaveChangesAsync(ct);
        return await _queries.GetAsync(taskId, ct);
    }

    public async Task<Result> ReorderQueueAsync(
        long userId, IReadOnlyList<long> taskIdsInOrder, CancellationToken ct = default)
    {
        var tasks = await _db.Tasks
            .Where(t => t.PrimaryAssigneeUserId == userId && taskIdsInOrder.Contains(t.Id))
            .ToListAsync(ct);

        // Reordering a task that is not yours would silently move someone else's work.
        if (tasks.Count != taskIdsInOrder.Distinct().Count())
            return Result.Failure(Error.Validation(
                "queue.unknown_task", "The list contains tasks that are not assigned to this user."));

        for (var position = 0; position < taskIdsInOrder.Count; position++)
        {
            var task = tasks.First(t => t.Id == taskIdsInOrder[position]);
            task.QueueOrder = position + 1;
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Names, not ids.
    ///
    /// This text goes into <c>TaskActivity</c>, which is the stream written for people to read —
    /// the readable half of the history split. "Assigned to user 36" is the schema talking, and a
    /// reader cannot act on it without going and looking up who 36 is.
    /// </summary>
    private async Task<string> DescribeAssignmentAsync(
        long? from, long? to, string? reason, CancellationToken ct)
    {
        var ids = new[] { from, to }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

        var names = ids.Count == 0
            ? new Dictionary<long, string>()
            : await _db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        // A deleted or renamed account must not make the line unreadable, so the id is the
        // fallback rather than the default.
        string Name(long? id) =>
            id is { } value && names.TryGetValue(value, out var name) ? name : $"user {id}";

        var text = (from, to) switch
        {
            (null, not null) => $"Given to {Name(to)}",
            (not null, null) => $"Taken off {Name(from)}",
            (not null, not null) => $"Moved from {Name(from)} to {Name(to)}",
            _ => "Assignment unchanged"
        };

        return string.IsNullOrWhiteSpace(reason) ? $"{text}." : $"{text}: {reason}";
    }
}
