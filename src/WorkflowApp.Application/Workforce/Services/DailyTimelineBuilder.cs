using WorkflowApp.Application.Workforce.Dtos;
using WorkflowApp.Domain.Entities.Workforce;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Workflow;

namespace WorkflowApp.Application.Workforce.Services;

/// <summary>
/// Turns the append-only <see cref="ActivityEvent"/> stream into a day's timeline.
///
/// The events record instants ("13:02 Lunch Started"); a timeline needs intervals
/// ("13:02–13:35 Lunch, 33m"). Each event therefore runs until the next one, and the final event
/// runs until whichever comes first: the end of the day, or now. That last clamp is what stops a
/// yesterday report from showing a still-running lunch break.
///
/// Pure by design — no database, no clock — so every edge case is directly testable.
/// </summary>
public static class DailyTimelineBuilder
{
    /// <param name="events">The day's events for one user, any order.</param>
    /// <param name="dayStart">UTC instant the business day begins.</param>
    /// <param name="dayEndExclusive">UTC instant the business day ends.</param>
    /// <param name="now">Current instant, used to close the trailing entry when the day is today.</param>
    /// <param name="carriedState">
    /// The state the user was already in when the day began, from the last event *before*
    /// <paramref name="dayStart"/>. Without it an overnight shift would appear to start at the first
    /// event after midnight and the hours before it would vanish from the report.
    /// </param>
    public static (IReadOnlyList<TimelineEntryDto> Entries,
                   TimeSpan TotalOnShift,
                   TimeSpan TotalProductive,
                   TimeSpan TotalAway,
                   IReadOnlyDictionary<string, TimeSpan> TimeByState) Build(
        IEnumerable<ActivityEvent> events,
        DateTimeOffset dayStart,
        DateTimeOffset dayEndExclusive,
        DateTimeOffset now,
        ActivityEvent? carriedState = null)
    {
        var ordered = events
            .Where(e => e.OccurredAt >= dayStart && e.OccurredAt < dayEndExclusive)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToList();

        // The timeline can only be drawn up to the present.
        var horizon = now < dayEndExclusive ? now : dayEndExclusive;

        var entries = new List<TimelineEntryDto>();

        // A state carried in from before midnight occupies the day from its start.
        if (carriedState is not null)
        {
            var carriedEnd = ordered.Count > 0 ? ordered[0].OccurredAt : horizon;
            if (carriedEnd > dayStart)
            {
                entries.Add(new TimelineEntryDto(
                    From: dayStart,
                    To: carriedEnd,
                    Duration: carriedEnd - dayStart,
                    Label: carriedState.Label,
                    State: carriedState.ResultingState,
                    RelatedTaskId: carriedState.RelatedTaskId,
                    Note: carriedState.Note,
                    IsOpen: ordered.Count == 0 && horizon < dayEndExclusive));
            }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];
            var isLast = i == ordered.Count - 1;

            var to = isLast ? horizon : ordered[i + 1].OccurredAt;

            // Guard against an event recorded after the horizon (clock skew, or a backfilled
            // event): a negative-length entry would corrupt every total.
            if (to < current.OccurredAt)
                to = current.OccurredAt;

            entries.Add(new TimelineEntryDto(
                From: current.OccurredAt,
                To: to,
                Duration: to - current.OccurredAt,
                Label: current.Label,
                State: current.ResultingState,
                RelatedTaskId: current.RelatedTaskId,
                Note: current.Note,
                IsOpen: isLast && horizon < dayEndExclusive));
        }

        var timeByState = new Dictionary<string, TimeSpan>();
        var onShift = TimeSpan.Zero;
        var productive = TimeSpan.Zero;
        var away = TimeSpan.Zero;

        foreach (var entry in entries)
        {
            // Events with no resulting state (task echoes such as "Task TSK-120 Started") appear on
            // the timeline but must not be double-counted against attendance totals.
            if (entry.State is not { } state) continue;

            timeByState[state.ToString()] =
                timeByState.TryGetValue(state.ToString(), out var existing)
                    ? existing + entry.Duration
                    : entry.Duration;

            if (WorkforceStateMachine.IsOnShift(state)) onShift += entry.Duration;
            if (WorkforceStateMachine.IsProductive(state)) productive += entry.Duration;
            if (WorkforceStateMachine.IsAway(state)) away += entry.Duration;
        }

        return (entries, onShift, productive, away, timeByState);
    }

    /// <summary>Display text for a state, e.g. <c>TemporarilyAway</c> → "Temporarily Away".</summary>
    public static string Humanize(WorkforceState state) => state switch
    {
        WorkforceState.NotLoggedIn => "Not Logged In",
        WorkforceState.LoggedInShiftNotStarted => "Logged In — Shift Not Started",
        WorkforceState.TemporarilyAway => "Temporarily Away",
        WorkforceState.ShiftEnded => "Shift Ended",
        _ => state.ToString()
    };
}
