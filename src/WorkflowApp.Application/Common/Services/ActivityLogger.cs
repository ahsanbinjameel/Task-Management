using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Domain.Entities.Workforce;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Common.Services;

/// <summary>
/// Appends to the workforce activity timeline (<see cref="ActivityEvent"/>). This is the
/// human-readable "what happened when" stream a daily report is built from — distinct from both
/// <see cref="IAuditService"/> (technical/security) and the per-task business timeline.
///
/// Task events are echoed here too, so one ordered timeline can show
/// "13:02 Lunch Started" next to "14:10 Task TSK-120 Started".
/// </summary>
public interface IActivityLogger
{
    /// <summary>Stages an event. The caller's <c>SaveChangesAsync</c> commits it in the same transaction.</summary>
    ActivityEvent Record(
        long userId,
        string label,
        WorkforceState? resultingState = null,
        long? shiftSessionId = null,
        long? relatedTaskId = null,
        string? note = null,
        DateTimeOffset? occurredAt = null);
}

public sealed class ActivityLogger : IActivityLogger
{
    private readonly IWorkflowDbContext _db;
    private readonly IDateTimeProvider _clock;

    public ActivityLogger(IWorkflowDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public ActivityEvent Record(
        long userId,
        string label,
        WorkforceState? resultingState = null,
        long? shiftSessionId = null,
        long? relatedTaskId = null,
        string? note = null,
        DateTimeOffset? occurredAt = null)
    {
        var activityEvent = new ActivityEvent
        {
            UserId = userId,
            Label = label,
            ResultingState = resultingState,
            ShiftSessionId = shiftSessionId,
            RelatedTaskId = relatedTaskId,
            Note = note,
            // Overridable so backfills (a stale shift closed at its last known-good moment) land
            // at the time they actually happened, not when the sweep noticed.
            OccurredAt = occurredAt ?? _clock.UtcNow
        };

        _db.ActivityEvents.Add(activityEvent);
        return activityEvent;
    }
}

/// <summary>Timeline labels that are not produced by a state transition.</summary>
public static class ActivityLabels
{
    public const string LoggedIn = "Logged In";
    public const string LoggedOut = "Logged Out";
    public const string ShiftClosedAutomatically = "Shift Closed Automatically";
    public const string ShiftForceEnded = "Shift Ended by Supervisor";
}
