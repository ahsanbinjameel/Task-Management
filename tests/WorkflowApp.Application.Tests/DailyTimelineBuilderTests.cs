using WorkflowApp.Application.Workforce.Services;
using WorkflowApp.Domain.Entities.Workforce;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

public class DailyTimelineBuilderTests
{
    private static readonly DateTimeOffset DayStart = new(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayEnd = DayStart.AddDays(1);

    private static ActivityEvent Event(int hour, int minute, string label, WorkforceState? state, long id = 0) =>
        new()
        {
            Id = id,
            OccurredAt = DayStart.AddHours(hour).AddMinutes(minute),
            Label = label,
            ResultingState = state
        };

    [Fact]
    public void Empty_day_produces_no_entries_and_no_time()
    {
        var (entries, onShift, productive, away, byState) = DailyTimelineBuilder.Build(
            Array.Empty<ActivityEvent>(), DayStart, DayEnd, DayEnd);

        Assert.Empty(entries);
        Assert.Equal(TimeSpan.Zero, onShift);
        Assert.Equal(TimeSpan.Zero, productive);
        Assert.Equal(TimeSpan.Zero, away);
        Assert.Empty(byState);
    }

    [Fact]
    public void Each_event_runs_until_the_next_one()
    {
        var events = new[]
        {
            Event(9, 0, "Shift Started", WorkforceState.Available, 1),
            Event(9, 30, "Started Working", WorkforceState.Working, 2),
            Event(12, 30, "Lunch Started", WorkforceState.Lunch, 3),
            Event(13, 0, "Resumed Working", WorkforceState.Working, 4),
            Event(17, 0, "Shift Ended", WorkforceState.ShiftEnded, 5)
        };

        var (entries, onShift, productive, away, byState) =
            DailyTimelineBuilder.Build(events, DayStart, DayEnd, DayEnd);

        Assert.Equal(5, entries.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), entries[0].Duration);   // 09:00 → 09:30 Available
        Assert.Equal(TimeSpan.FromHours(3), entries[1].Duration);      // 09:30 → 12:30 Working
        Assert.Equal(TimeSpan.FromMinutes(30), entries[2].Duration);   // 12:30 → 13:00 Lunch
        Assert.Equal(TimeSpan.FromHours(4), entries[3].Duration);      // 13:00 → 17:00 Working

        Assert.Equal(TimeSpan.FromHours(7), productive);               // 3h + 4h
        Assert.Equal(TimeSpan.FromMinutes(30), away);
        Assert.Equal(TimeSpan.FromHours(8), onShift);                  // 09:00 → 17:00
        Assert.Equal(TimeSpan.FromHours(7), byState[nameof(WorkforceState.Working)]);
    }

    [Fact]
    public void Shift_ended_time_is_not_counted_as_on_shift()
    {
        var events = new[]
        {
            Event(9, 0, "Shift Started", WorkforceState.Available, 1),
            Event(17, 0, "Shift Ended", WorkforceState.ShiftEnded, 2)
        };

        var (_, onShift, _, _, _) = DailyTimelineBuilder.Build(events, DayStart, DayEnd, DayEnd);

        // The trailing ShiftEnded entry spans 17:00 → midnight but must contribute nothing.
        Assert.Equal(TimeSpan.FromHours(8), onShift);
    }

    [Fact]
    public void An_open_state_is_measured_only_up_to_now_not_to_the_end_of_the_day()
    {
        var now = DayStart.AddHours(11);
        var events = new[]
        {
            Event(9, 0, "Shift Started", WorkforceState.Available, 1),
            Event(9, 30, "Started Working", WorkforceState.Working, 2)
        };

        var (entries, onShift, productive, _, _) =
            DailyTimelineBuilder.Build(events, DayStart, DayEnd, now);

        Assert.Equal(TimeSpan.FromMinutes(90), productive);   // 09:30 → 11:00, not → midnight
        Assert.Equal(TimeSpan.FromHours(2), onShift);
        Assert.True(entries[^1].IsOpen);
        Assert.False(entries[0].IsOpen);
    }

    [Fact]
    public void A_finished_past_day_has_no_open_entry()
    {
        var events = new[] { Event(9, 0, "Shift Started", WorkforceState.Available, 1) };

        // "Now" is well past the day being reported on.
        var (entries, _, _, _, _) = DailyTimelineBuilder.Build(events, DayStart, DayEnd, DayEnd.AddDays(3));

        Assert.All(entries, e => Assert.False(e.IsOpen));
        Assert.Equal(DayEnd, entries[^1].To);
    }

    [Fact]
    public void Events_outside_the_day_are_ignored()
    {
        var events = new[]
        {
            new ActivityEvent { Id = 1, OccurredAt = DayStart.AddHours(-2), Label = "Yesterday", ResultingState = WorkforceState.Working },
            Event(9, 0, "Shift Started", WorkforceState.Available, 2),
            new ActivityEvent { Id = 3, OccurredAt = DayEnd.AddHours(1), Label = "Tomorrow", ResultingState = WorkforceState.Working }
        };

        var (entries, _, _, _, _) = DailyTimelineBuilder.Build(events, DayStart, DayEnd, DayEnd);

        Assert.Single(entries);
        Assert.Equal("Shift Started", entries[0].Label);
    }

    [Fact]
    public void Unordered_events_are_sorted_before_the_timeline_is_built()
    {
        var events = new[]
        {
            Event(12, 30, "Lunch Started", WorkforceState.Lunch, 3),
            Event(9, 0, "Shift Started", WorkforceState.Available, 1),
            Event(9, 30, "Started Working", WorkforceState.Working, 2)
        };

        var (entries, _, productive, _, _) = DailyTimelineBuilder.Build(events, DayStart, DayEnd, DayEnd);

        Assert.Equal(new[] { "Shift Started", "Started Working", "Lunch Started" },
            entries.Select(e => e.Label));
        Assert.Equal(TimeSpan.FromHours(3), productive);
    }

    [Fact]
    public void Simultaneous_events_are_ordered_by_id_so_the_result_is_deterministic()
    {
        var events = new[]
        {
            Event(9, 0, "Second", WorkforceState.Working, 2),
            Event(9, 0, "First", WorkforceState.Available, 1)
        };

        var (entries, _, _, _, _) = DailyTimelineBuilder.Build(events, DayStart, DayEnd, DayEnd);

        Assert.Equal(new[] { "First", "Second" }, entries.Select(e => e.Label));
    }

    [Fact]
    public void Events_without_a_state_appear_on_the_timeline_but_are_not_counted()
    {
        var events = new[]
        {
            Event(9, 0, "Shift Started", WorkforceState.Available, 1),
            // A task echo — visible in the timeline, but it must not double-count attendance.
            new ActivityEvent { Id = 2, OccurredAt = DayStart.AddHours(10), Label = "Task TSK-120 Started", ResultingState = null, RelatedTaskId = 120 },
            Event(17, 0, "Shift Ended", WorkforceState.ShiftEnded, 3)
        };

        var (entries, onShift, _, _, byState) = DailyTimelineBuilder.Build(events, DayStart, DayEnd, DayEnd);

        Assert.Equal(3, entries.Count);
        Assert.Equal(120, entries[1].RelatedTaskId);
        // Only the 09:00→10:00 Available stretch counts; the untyped hour contributes nothing.
        Assert.Equal(TimeSpan.FromHours(1), onShift);
        Assert.Equal(TimeSpan.FromHours(1), byState[nameof(WorkforceState.Available)]);
    }

    [Fact]
    public void A_state_carried_over_from_the_previous_day_fills_the_start_of_the_day()
    {
        // Night shift: still Working at midnight, first event of the new day at 02:00.
        var carried = new ActivityEvent
        {
            Id = 99,
            OccurredAt = DayStart.AddHours(-3),
            Label = "Started Working",
            ResultingState = WorkforceState.Working
        };

        var events = new[] { Event(2, 0, "Shift Ended", WorkforceState.ShiftEnded, 1) };

        var (entries, onShift, productive, _, _) =
            DailyTimelineBuilder.Build(events, DayStart, DayEnd, DayEnd, carried);

        Assert.Equal(2, entries.Count);
        Assert.Equal(DayStart, entries[0].From);
        // Without the carry-over these two hours would silently vanish from the report.
        Assert.Equal(TimeSpan.FromHours(2), productive);
        Assert.Equal(TimeSpan.FromHours(2), onShift);
    }

    [Fact]
    public void A_carried_state_with_no_events_at_all_spans_the_whole_day_up_to_now()
    {
        var carried = new ActivityEvent
        {
            Id = 99,
            OccurredAt = DayStart.AddHours(-5),
            Label = "Started Working",
            ResultingState = WorkforceState.Working
        };

        var now = DayStart.AddHours(6);

        var (entries, _, productive, _, _) =
            DailyTimelineBuilder.Build(Array.Empty<ActivityEvent>(), DayStart, DayEnd, now, carried);

        var entry = Assert.Single(entries);
        Assert.Equal(DayStart, entry.From);
        Assert.Equal(now, entry.To);
        Assert.True(entry.IsOpen);
        Assert.Equal(TimeSpan.FromHours(6), productive);
    }

    [Fact]
    public void An_event_recorded_after_now_never_produces_negative_time()
    {
        // Clock skew or a backfilled event: a negative span would corrupt every total.
        var now = DayStart.AddHours(8);
        var events = new[] { Event(10, 0, "Started Working", WorkforceState.Working, 1) };

        var (entries, onShift, productive, _, _) =
            DailyTimelineBuilder.Build(events, DayStart, DayEnd, now);

        Assert.Equal(TimeSpan.Zero, entries[0].Duration);
        Assert.Equal(TimeSpan.Zero, productive);
        Assert.Equal(TimeSpan.Zero, onShift);
    }

    [Theory]
    [InlineData(WorkforceState.TemporarilyAway, "Temporarily Away")]
    [InlineData(WorkforceState.LoggedInShiftNotStarted, "Logged In — Shift Not Started")]
    [InlineData(WorkforceState.NotLoggedIn, "Not Logged In")]
    [InlineData(WorkforceState.ShiftEnded, "Shift Ended")]
    [InlineData(WorkforceState.Lunch, "Lunch")]
    public void States_are_humanized_for_display(WorkforceState state, string expected) =>
        Assert.Equal(expected, DailyTimelineBuilder.Humanize(state));
}
