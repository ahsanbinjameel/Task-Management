using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Services;

/// <summary>
/// Applies a status change and appends the three trails every transition must leave: the
/// append-only <see cref="StatusHistory"/>, the human-readable <see cref="TaskActivity"/>, and the
/// echo onto the actor's workforce timeline. Rows are staged only — the caller commits.
///
/// Services that own a slice of the lifecycle (the timer, QC, closure) move tasks themselves rather
/// than routing through <see cref="TaskWorkflowService"/>, because each has extra records to write
/// in the same transaction. This exists so they all leave an identical trail.
/// </summary>
internal static class TaskStatusJournal
{
    public static void Write(
        IWorkflowDbContext db,
        IActivityLogger activity,
        WorkTask task,
        WorkTaskStatus to,
        long actorUserId,
        DateTimeOffset now,
        string? reason,
        ActivityType activityType,
        string description)
    {
        var from = task.Status;
        task.Status = to;

        db.StatusHistories.Add(new StatusHistory
        {
            TaskId = task.Id,
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = actorUserId,
            ChangedAt = now,
            Reason = reason
        });

        db.TaskActivities.Add(new TaskActivity
        {
            TaskId = task.Id,
            Type = activityType,
            ActorUserId = actorUserId,
            OccurredAt = now,
            Description = description
        });

        activity.Record(
            actorUserId,
            $"Task {task.TaskNumber} — {TaskWorkflowService.Humanize(to)}",
            resultingState: null,
            relatedTaskId: task.Id,
            note: reason,
            occurredAt: now);
    }
}
