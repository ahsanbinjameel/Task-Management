using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Workflow;
using Xunit;

namespace WorkflowApp.Domain.Tests;

public class WorkforceStateMachineTests
{
    [Fact]
    public void Cannot_go_on_shift_without_logging_in_first()
    {
        Assert.False(WorkforceStateMachine.IsAllowed(WorkforceState.NotLoggedIn, WorkforceState.Available));
        Assert.False(WorkforceStateMachine.IsAllowed(WorkforceState.NotLoggedIn, WorkforceState.Working));
        Assert.True(WorkforceStateMachine.IsAllowed(WorkforceState.NotLoggedIn, WorkforceState.LoggedInShiftNotStarted));
    }

    [Fact]
    public void Logging_in_does_not_put_the_user_on_shift()
    {
        // The core distinction: authenticated is not the same as working.
        Assert.False(WorkforceStateMachine.IsOnShift(WorkforceState.LoggedInShiftNotStarted));
        Assert.True(WorkforceStateMachine.IsOnShift(WorkforceState.Available));
    }

    [Fact]
    public void Shift_starts_from_logged_in_or_from_a_previous_finished_shift()
    {
        Assert.True(WorkforceStateMachine.IsAllowed(WorkforceState.LoggedInShiftNotStarted, WorkforceState.Available));
        // Split shifts: a second shift the same day.
        Assert.True(WorkforceStateMachine.IsAllowed(WorkforceState.ShiftEnded, WorkforceState.Available));
    }

    [Theory]
    [InlineData(WorkforceState.Break)]
    [InlineData(WorkforceState.Lunch)]
    [InlineData(WorkforceState.Meeting)]
    [InlineData(WorkforceState.TemporarilyAway)]
    public void Away_states_are_reachable_from_both_available_and_working_and_lead_back(WorkforceState away)
    {
        Assert.True(WorkforceStateMachine.IsAllowed(WorkforceState.Available, away));
        Assert.True(WorkforceStateMachine.IsAllowed(WorkforceState.Working, away));
        Assert.True(WorkforceStateMachine.IsAllowed(away, WorkforceState.Available));
        // Straight back into the task, without passing through Available.
        Assert.True(WorkforceStateMachine.IsAllowed(away, WorkforceState.Working));
        Assert.True(WorkforceStateMachine.IsAway(away));
    }

    [Fact]
    public void Only_working_counts_as_productive()
    {
        Assert.True(WorkforceStateMachine.IsProductive(WorkforceState.Working));

        foreach (var state in Enum.GetValues<WorkforceState>().Where(s => s != WorkforceState.Working))
            Assert.False(WorkforceStateMachine.IsProductive(state));
    }

    [Fact]
    public void Working_and_shift_ended_are_not_self_service_targets()
    {
        // Working is entered by starting a task; ShiftEnded by ending the shift. Neither may be
        // claimed directly, or availability would stop reflecting reality.
        Assert.False(WorkforceStateMachine.IsSelfServiceTarget(WorkforceState.Working));
        Assert.False(WorkforceStateMachine.IsSelfServiceTarget(WorkforceState.ShiftEnded));
        Assert.False(WorkforceStateMachine.IsSelfServiceTarget(WorkforceState.NotLoggedIn));

        Assert.True(WorkforceStateMachine.IsSelfServiceTarget(WorkforceState.Available));
        Assert.True(WorkforceStateMachine.IsSelfServiceTarget(WorkforceState.Lunch));
    }

    [Fact]
    public void Self_service_next_states_exclude_working_even_though_it_is_reachable()
    {
        var reachable = WorkforceStateMachine.NextStates(WorkforceState.Available).ToList();
        var offered = WorkforceStateMachine.SelfServiceNextStates(WorkforceState.Available).ToList();

        Assert.Contains(WorkforceState.Working, reachable);
        Assert.DoesNotContain(WorkforceState.Working, offered);
        Assert.DoesNotContain(WorkforceState.ShiftEnded, offered);
        Assert.Contains(WorkforceState.Lunch, offered);
    }

    [Theory]
    [InlineData(WorkforceState.Available)]
    [InlineData(WorkforceState.Working)]
    [InlineData(WorkforceState.Break)]
    [InlineData(WorkforceState.Lunch)]
    [InlineData(WorkforceState.Meeting)]
    [InlineData(WorkforceState.TemporarilyAway)]
    public void A_shift_can_be_ended_from_any_on_shift_state(WorkforceState from)
    {
        Assert.True(WorkforceStateMachine.IsAllowed(from, WorkforceState.ShiftEnded));
    }

    [Fact]
    public void Ended_shift_cannot_jump_back_into_work_or_a_break()
    {
        Assert.False(WorkforceStateMachine.IsAllowed(WorkforceState.ShiftEnded, WorkforceState.Working));
        Assert.False(WorkforceStateMachine.IsAllowed(WorkforceState.ShiftEnded, WorkforceState.Lunch));
        // Only: start a fresh shift, or log out.
        Assert.True(WorkforceStateMachine.IsAllowed(WorkforceState.ShiftEnded, WorkforceState.NotLoggedIn));
    }

    [Fact]
    public void Every_transition_carries_a_timeline_label()
    {
        Assert.All(WorkforceStateMachine.Transitions, t => Assert.False(string.IsNullOrWhiteSpace(t.Label)));
    }

    [Fact]
    public void No_transition_is_declared_twice_and_none_is_a_self_loop()
    {
        var pairs = WorkforceStateMachine.Transitions.Select(t => (t.From, t.To)).ToList();

        Assert.Equal(pairs.Count, pairs.Distinct().Count());
        Assert.DoesNotContain(pairs, p => p.From == p.To);
    }

    [Fact]
    public void The_same_destination_reads_the_same_way_regardless_of_origin()
    {
        // "Lunch Started" whether they came from Available or from Working — the timeline should
        // not expose which internal state preceded it.
        var fromAvailable = WorkforceStateMachine.Find(WorkforceState.Available, WorkforceState.Lunch)!;
        var fromWorking = WorkforceStateMachine.Find(WorkforceState.Working, WorkforceState.Lunch)!;

        Assert.Equal(fromAvailable.Label, fromWorking.Label);
    }

    [Fact]
    public void Every_on_shift_state_can_reach_shift_ended_so_no_one_gets_stuck()
    {
        var onShift = Enum.GetValues<WorkforceState>().Where(WorkforceStateMachine.IsOnShift);

        Assert.All(onShift, state =>
            Assert.True(WorkforceStateMachine.IsAllowed(state, WorkforceState.ShiftEnded),
                $"{state} has no way to end the shift."));
    }
}
