using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Services;

public interface ITaskCreationService
{
    /// <summary>
    /// Creates the executable task for an approved request. Staged, not saved — the caller commits
    /// it together with the approval so a task can never exist for a request that was not approved.
    /// </summary>
    Task<WorkTask> CreateFromRequestAsync(
        Request request,
        long approvedByUserId,
        Priority approvedPriority,
        decimal? estimatedEffortHours,
        DateTimeOffset? dueDate,
        string? acceptanceCriteria,
        CancellationToken ct = default);

    /// <summary>
    /// Breaks an existing task down. The subtask is a full task with its own number, assignee,
    /// timer and history — not a checklist row — because the work it represents has to be
    /// schedulable and reportable in its own right.
    /// </summary>
    Task<Result<WorkTask>> CreateSubtaskAsync(
        long parentTaskId, long actingUserId, CreateSubtaskDto request, CancellationToken ct = default);
}

/// <summary>
/// The single place a <see cref="WorkTask"/> comes into existence. That is what makes the "a request
/// never auto-becomes a task" rule auditable rather than hopeful: new work enters the system through
/// this class or not at all.
///
/// There are exactly two ways in. Triage approval creates the task for a request. Subtask creation
/// breaks down a task that already exists — which cannot smuggle an unapproved request into
/// execution, because it starts from approved work rather than from intake.
/// </summary>
public sealed class TaskCreationService : ITaskCreationService
{
    private readonly IWorkflowDbContext _db;
    private readonly INumberGenerator _numbers;
    private readonly IDateTimeProvider _clock;

    public TaskCreationService(IWorkflowDbContext db, INumberGenerator numbers, IDateTimeProvider clock)
    {
        _db = db;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<WorkTask> CreateFromRequestAsync(
        Request request,
        long approvedByUserId,
        Priority approvedPriority,
        decimal? estimatedEffortHours,
        DateTimeOffset? dueDate,
        string? acceptanceCriteria,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var task = new WorkTask
        {
            TaskNumber = await _numbers.NextAsync(NumberSequences.Task, NumberSequences.TaskPrefix, ct),

            // Provenance: the task can always be traced back to what was asked for.
            RequestId = request.Id,

            Title = request.Title,
            Description = request.Description,
            Type = request.Type,
            ProjectId = request.ProjectId,
            ClientId = request.ClientId,
            ModuleId = request.ModuleId,

            // The approved priority, not the requested urgency.
            Priority = approvedPriority,

            EstimatedEffortHours = estimatedEffortHours,
            DueDate = dueDate ?? request.TargetDate,
            AcceptanceCriteria = acceptanceCriteria,

            // Born ready to be scheduled, with nobody assigned yet.
            Status = WorkTaskStatus.ReadyForAssignment
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(ct);   // need the id before history rows can reference it

        _db.StatusHistories.Add(new StatusHistory
        {
            TaskId = task.Id,
            FromStatus = WorkTaskStatus.Approved,
            ToStatus = WorkTaskStatus.ReadyForAssignment,
            ChangedByUserId = approvedByUserId,
            ChangedAt = now,
            Reason = $"Created from request {request.RequestNumber}"
        });

        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = task.Id,
            Type = ActivityType.TaskCreated,
            ActorUserId = approvedByUserId,
            OccurredAt = now,
            Description = $"Task created from approved request {request.RequestNumber} " +
                          $"with priority {approvedPriority}."
        });

        return task;
    }

    public async Task<Result<WorkTask>> CreateSubtaskAsync(
        long parentTaskId, long actingUserId, CreateSubtaskDto request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var parent = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == parentTaskId, ct);
        if (parent is null)
            return Result<WorkTask>.Failure(Error.NotFound("task.not_found", "Parent task not found."));

        if (parent.Status is WorkTaskStatus.Closed or WorkTaskStatus.Cancelled or WorkTaskStatus.Duplicate)
            return Result<WorkTask>.Failure(Error.Conflict(
                "subtask.parent_finished", "Work cannot be added to a task that is already finished."));

        // One level. A tree of subtasks makes "is the parent done?" unanswerable at a glance, and
        // the closure check would have to recurse to stay honest.
        if (parent.ParentTaskId is not null)
            return Result<WorkTask>.Failure(Error.Conflict(
                "subtask.nesting_not_allowed", "A subtask cannot itself be broken down."));

        if (request.AssigneeUserId is { } assigneeId &&
            !await _db.Users.AnyAsync(u => u.Id == assigneeId && u.IsActive, ct))
        {
            return Result<WorkTask>.Failure(Error.Validation(
                "task.assignee_not_found", "That assignee does not exist or is inactive."));
        }

        var subtask = new WorkTask
        {
            TaskNumber = await _numbers.NextAsync(NumberSequences.Task, NumberSequences.TaskPrefix, ct),
            ParentTaskId = parent.Id,
            IsRequired = request.IsRequired,

            // Provenance follows the parent, so a subtask still traces back to what was asked for.
            RequestId = parent.RequestId,

            Title = request.Title,
            Description = request.Description,
            Type = parent.Type,
            ProjectId = parent.ProjectId,
            ClientId = parent.ClientId,
            ModuleId = parent.ModuleId,

            Priority = request.Priority ?? parent.Priority,
            EstimatedEffortHours = request.EstimatedEffortHours,
            DueDate = request.DueDate ?? parent.DueDate,
            AcceptanceCriteria = request.AcceptanceCriteria,

            PrimaryAssigneeUserId = request.AssigneeUserId,
            Status = request.AssigneeUserId is null
                ? WorkTaskStatus.ReadyForAssignment
                : WorkTaskStatus.Assigned
        };

        _db.Tasks.Add(subtask);
        await _db.SaveChangesAsync(ct);   // need the id before history rows can reference it

        _db.StatusHistories.Add(new StatusHistory
        {
            TaskId = subtask.Id,
            FromStatus = WorkTaskStatus.Approved,
            ToStatus = subtask.Status,
            ChangedByUserId = actingUserId,
            ChangedAt = now,
            Reason = $"Subtask of {parent.TaskNumber}"
        });

        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = subtask.Id,
            Type = ActivityType.TaskCreated,
            ActorUserId = actingUserId,
            OccurredAt = now,
            Description = $"Created as a subtask of {parent.TaskNumber}."
        });

        // Visible from the parent too, so breaking work down shows up on its timeline.
        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = parent.Id,
            Type = ActivityType.SubtaskCreated,
            ActorUserId = actingUserId,
            OccurredAt = now,
            Description = $"Subtask {subtask.TaskNumber} created: {subtask.Title}"
        });

        if (request.AssigneeUserId is { } newAssignee)
        {
            _db.AssignmentHistories.Add(new AssignmentHistory
            {
                TaskId = subtask.Id,
                FromUserId = null,
                ToUserId = newAssignee,
                AssignedByUserId = actingUserId,
                AssignedAt = now
            });
        }

        await _db.SaveChangesAsync(ct);
        return Result<WorkTask>.Success(subtask);
    }
}
