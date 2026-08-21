using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Services;
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
}

/// <summary>
/// The single place a <see cref="WorkTask"/> comes into existence. Keeping it to one method makes
/// the "a request never auto-becomes a task" rule auditable: there is exactly one caller, and it
/// is the approval branch of triage.
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
}
