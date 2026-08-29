using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

public class ShiftServiceTests
{
    private const string Password = "CorrectHorse1";

    /// <summary>A harness with a user who has logged in, so they are ready to start a shift.</summary>
    private static async Task<(TestHarness Harness, long UserId)> LoggedInAsync()
    {
        var h = new TestHarness();
        await h.SeedRolesAndPermissionsAsync();
        // Shifts are only tracked for people who work on tasks, so the subject has to be one.
        var user = await h.CreateUserAsync(roles: DefaultRoles.Worker);
        await h.Auth.LoginAsync(new LoginRequest { UserName = user.UserName, Password = Password });
        return (h, user.Id);
    }

    private static async Task<(TestHarness Harness, long UserId)> OnShiftAsync()
    {
        var (h, userId) = await LoggedInAsync();
        var result = await h.Shifts.StartShiftAsync(userId);
        Assert.True(result.IsSuccess);
        return (h, userId);
    }

    [Fact]
    public async Task Starting_a_shift_opens_a_session_and_makes_the_user_available()
    {
        var (h, userId) = await LoggedInAsync();
        using var _ = h;

        var result = await h.Shifts.StartShiftAsync(userId);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkforceState.Available, result.Value!.State);
        Assert.True(result.Value.IsOnShift);
        Assert.NotNull(result.Value.CurrentShift);
        Assert.Null(result.Value.CurrentShift!.ShiftEnd);

        var shift = await h.Db.ShiftSessions.SingleAsync();
        Assert.Equal(userId, shift.UserId);
        // Captured for the audit trail.
        Assert.Equal("127.0.0.1", shift.StartIpAddress);
    }

    /// <summary>
    /// Which is why anything that mints a token outside <c>AuthService.LoginAsync</c> has to move
    /// the user out of <c>NotLoggedIn</c> itself. Demo mode did not, and stranded its whole cast
    /// here: no shift could be opened, so no task timer could be started —
    /// see <c>IDemoEnvironment.SignInAsync</c>.
    /// </summary>
    [Fact]
    public async Task Starting_a_shift_requires_being_logged_in_first()
    {
        var h = new TestHarness();
        using var _ = h;
        await h.SeedRolesAndPermissionsAsync();
        var user = await h.CreateUserAsync(roles: DefaultRoles.Worker);   // never logged in → NotLoggedIn

        var result = await h.Shifts.StartShiftAsync(user.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("workforce.transition_not_allowed", result.Error!.Code);
        Assert.False(await h.Db.ShiftSessions.AnyAsync());
    }

    [Theory]
    [InlineData(DefaultRoles.Reviewer)]
    [InlineData(DefaultRoles.AssignmentManager)]
    [InlineData(DefaultRoles.Requester)]
    [InlineData(DefaultRoles.Management)]
    public async Task People_who_do_not_execute_tasks_are_not_on_the_clock(string role)
    {
        var h = new TestHarness();
        using var _ = h;
        await h.SeedRolesAndPermissionsAsync();

        var user = await h.CreateUserAsync("nonworker", roles: role);
        await h.Auth.LoginAsync(new LoginRequest { UserName = "nonworker", Password = Password });

        var result = await h.Shifts.StartShiftAsync(user.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("shift.not_tracked", result.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        Assert.False(await h.Db.ShiftSessions.AnyAsync());
    }

    [Fact]
    public async Task An_untracked_user_is_offered_no_shift_controls()
    {
        var h = new TestHarness();
        using var _ = h;
        await h.SeedRolesAndPermissionsAsync();

        var reviewer = await h.CreateUserAsync("victor", roles: DefaultRoles.Reviewer);
        var worker = await h.CreateUserAsync("wu", roles: DefaultRoles.Worker);

        var reviewerStatus = await h.Shifts.GetStatusAsync(reviewer.Id);
        Assert.False(reviewerStatus.Value!.IsShiftTracked);
        // A client should hide the panel rather than offer a call that will be refused.
        Assert.Empty(reviewerStatus.Value.AvailableStates);

        var workerStatus = await h.Shifts.GetStatusAsync(worker.Id);
        Assert.True(workerStatus.Value!.IsShiftTracked);
    }

    [Fact]
    public async Task Losing_the_permission_mid_shift_still_lets_the_user_clock_out()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        // Roles changed while they were on shift.
        await h.UserAdmin.AssignRolesAsync(userId, Array.Empty<string>());

        h.Clock.Advance(TimeSpan.FromHours(4));
        var ended = await h.Shifts.EndShiftAsync(userId, "Role changed mid-shift");

        // Otherwise they would be stuck with an open shift needing a supervisor to close it.
        Assert.True(ended.IsSuccess);
        Assert.NotNull((await h.Db.ShiftSessions.SingleAsync()).ShiftEnd);

        // But they cannot open a new one.
        var restart = await h.Shifts.StartShiftAsync(userId);
        Assert.Equal("shift.not_tracked", restart.Error!.Code);
    }

    [Fact]
    public async Task A_second_shift_cannot_be_opened_while_one_is_running()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        var second = await h.Shifts.StartShiftAsync(userId);

        Assert.True(second.IsFailure);
        Assert.Equal("shift.already_open", second.Error!.Code);
        Assert.Equal(ErrorType.Conflict, second.Error.Type);
        Assert.Equal(1, await h.Db.ShiftSessions.CountAsync());
    }

    [Fact]
    public async Task Ending_a_shift_closes_it_and_records_the_duration()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        h.Clock.Advance(TimeSpan.FromHours(8));

        var result = await h.Shifts.EndShiftAsync(userId, "Done for today");

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkforceState.ShiftEnded, result.Value!.State);
        Assert.False(result.Value.IsOnShift);

        var shift = await h.Db.ShiftSessions.SingleAsync();
        Assert.NotNull(shift.ShiftEnd);
        Assert.Equal(TimeSpan.FromHours(8), shift.ShiftEnd!.Value - shift.ShiftStart);
        Assert.False(shift.EndedImproperly);
        Assert.Null(shift.EndedByUserId);
        Assert.Equal("Done for today", shift.EndNote);
    }

    [Fact]
    public async Task Ending_a_shift_that_is_not_open_is_refused()
    {
        var (h, userId) = await LoggedInAsync();
        using var _ = h;

        var result = await h.Shifts.EndShiftAsync(userId, null);

        Assert.True(result.IsFailure);
        Assert.Equal("shift.not_open", result.Error!.Code);
    }

    [Fact]
    public async Task A_shift_cannot_be_ended_while_a_task_work_session_is_still_running()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        h.Db.WorkSessions.Add(new WorkSession
        {
            TaskId = 1,
            UserId = userId,
            SessionStart = h.Clock.UtcNow,
            Status = WorkSessionStatus.Active
        });
        await h.Db.SaveChangesAsync();

        var result = await h.Shifts.EndShiftAsync(userId, null);

        // Otherwise the work session would be orphaned and its time lost.
        Assert.True(result.IsFailure);
        Assert.Equal("shift.work_session_active", result.Error!.Code);

        var shift = await h.Db.ShiftSessions.SingleAsync();
        Assert.Null(shift.ShiftEnd);
    }

    [Fact]
    public async Task After_ending_a_shift_a_new_one_can_be_started_the_same_day()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        h.Clock.Advance(TimeSpan.FromHours(4));
        await h.Shifts.EndShiftAsync(userId, null);

        h.Clock.Advance(TimeSpan.FromHours(2));
        var second = await h.Shifts.StartShiftAsync(userId);

        Assert.True(second.IsSuccess);
        Assert.Equal(2, await h.Db.ShiftSessions.CountAsync());
        Assert.Equal(1, await h.Db.ShiftSessions.CountAsync(s => s.ShiftEnd == null));
    }

    [Theory]
    [InlineData(WorkforceState.Break)]
    [InlineData(WorkforceState.Lunch)]
    [InlineData(WorkforceState.Meeting)]
    [InlineData(WorkforceState.TemporarilyAway)]
    public async Task A_user_can_step_away_and_come_back(WorkforceState away)
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        var stepAway = await h.Shifts.ChangeStateAsync(userId, away, "note");
        Assert.True(stepAway.IsSuccess);
        Assert.Equal(away, stepAway.Value!.State);
        Assert.True(stepAway.Value.IsOnShift);   // away is still on shift

        var back = await h.Shifts.ChangeStateAsync(userId, WorkforceState.Available, null);
        Assert.True(back.IsSuccess);
        Assert.Equal(WorkforceState.Available, back.Value!.State);
    }

    [Fact]
    public async Task A_user_cannot_declare_themselves_working()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        var result = await h.Shifts.ChangeStateAsync(userId, WorkforceState.Working, null);

        // Working must be a consequence of starting a task, never a claim.
        Assert.True(result.IsFailure);
        Assert.Equal("workforce.state_not_self_service", result.Error!.Code);
        Assert.Contains("starting a task", result.Error.Message);
    }

    [Fact]
    public async Task A_user_cannot_end_their_shift_through_the_state_endpoint()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        var result = await h.Shifts.ChangeStateAsync(userId, WorkforceState.ShiftEnded, null);

        Assert.True(result.IsFailure);
        Assert.Equal("workforce.state_not_self_service", result.Error!.Code);

        var shift = await h.Db.ShiftSessions.SingleAsync();
        Assert.Null(shift.ShiftEnd);
    }

    [Fact]
    public async Task Availability_cannot_be_changed_without_an_open_shift()
    {
        var (h, userId) = await LoggedInAsync();
        using var _ = h;

        var result = await h.Shifts.ChangeStateAsync(userId, WorkforceState.Lunch, null);

        Assert.True(result.IsFailure);
        Assert.Equal("shift.not_open", result.Error!.Code);
    }

    [Fact]
    public async Task Setting_the_state_the_user_is_already_in_is_a_no_op()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        var eventsBefore = await h.Db.ActivityEvents.CountAsync();
        var result = await h.Shifts.ChangeStateAsync(userId, WorkforceState.Available, null);

        Assert.True(result.IsSuccess);
        // No duplicate timeline entry for a state that did not change.
        Assert.Equal(eventsBefore, await h.Db.ActivityEvents.CountAsync());
    }

    [Fact]
    public async Task Every_state_change_is_written_to_the_activity_timeline()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        h.Clock.Advance(TimeSpan.FromHours(3));
        await h.Shifts.ChangeStateAsync(userId, WorkforceState.Lunch, null);
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        await h.Shifts.ChangeStateAsync(userId, WorkforceState.Available, null);
        h.Clock.Advance(TimeSpan.FromHours(4));
        await h.Shifts.EndShiftAsync(userId, null);

        var labels = await h.Db.ActivityEvents
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.OccurredAt)
            .Select(e => e.Label)
            .ToListAsync();

        Assert.Equal(
            new[] { ActivityLabels.LoggedIn, "Shift Started", "Lunch Started", "Lunch Ended", "Shift Ended" },
            labels);
    }

    [Fact]
    public async Task Status_offers_only_the_states_the_user_may_actually_pick()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        var status = await h.Shifts.GetStatusAsync(userId);

        Assert.True(status.IsSuccess);
        Assert.Contains(WorkforceState.Lunch, status.Value!.AvailableStates);
        Assert.DoesNotContain(WorkforceState.Working, status.Value.AvailableStates);
        Assert.DoesNotContain(WorkforceState.ShiftEnded, status.Value.AvailableStates);
    }

    [Fact]
    public async Task Status_reports_when_the_current_state_began()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        h.Clock.Advance(TimeSpan.FromHours(2));
        var lunchAt = h.Clock.UtcNow;
        await h.Shifts.ChangeStateAsync(userId, WorkforceState.Lunch, null);

        h.Clock.Advance(TimeSpan.FromMinutes(20));
        var status = await h.Shifts.GetStatusAsync(userId);

        Assert.Equal(lunchAt, status.Value!.StateSince);
    }

    [Fact]
    public async Task A_supervisor_can_force_end_an_abandoned_shift_and_it_is_flagged_and_audited()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        var supervisor = await h.CreateUserAsync("supervisor", "SupervisorPass1");
        h.Clock.Advance(TimeSpan.FromHours(10));

        var result = await h.Shifts.ForceEndShiftAsync(userId, supervisor.Id, "Employee went home without ending shift");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.EndedImproperly);
        Assert.Equal(supervisor.Id, result.Value.EndedByUserId);

        var user = await h.Db.Users.FirstAsync(u => u.Id == userId);
        Assert.Equal(WorkforceState.ShiftEnded, user.WorkforceState);

        Assert.True(await h.Db.AuditLogs.AnyAsync(a => a.Action == AuditActions.ShiftForceEnded));
        Assert.True(await h.Db.ActivityEvents.AnyAsync(e => e.Label == ActivityLabels.ShiftForceEnded));
    }

    [Fact]
    public async Task Force_ending_a_shift_requires_a_reason()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;
        var supervisor = await h.CreateUserAsync("supervisor", "SupervisorPass1");

        var result = await h.Shifts.ForceEndShiftAsync(userId, supervisor.Id, "   ");

        Assert.True(result.IsFailure);
        Assert.Equal("shift.reason_required", result.Error!.Code);
    }

    [Fact]
    public async Task Logging_out_mid_shift_leaves_the_shift_open()
    {
        var (h, userId) = await LoggedInAsync();
        using var _ = h;

        var login = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = Password });
        await h.Shifts.StartShiftAsync(userId);

        await h.Auth.LogoutAsync(new RefreshTokenRequest { RefreshToken = login.Value!.RefreshToken });

        // The auth session ended; the shift did not. The sweep decides what to do about it.
        var shift = await h.Db.ShiftSessions.SingleAsync();
        Assert.Null(shift.ShiftEnd);

        var user = await h.Db.Users.FirstAsync(u => u.Id == userId);
        Assert.Equal(WorkforceState.Available, user.WorkforceState);
    }

    [Fact]
    public async Task Logging_in_writes_a_timeline_entry_but_does_not_start_a_shift()
    {
        var (h, userId) = await LoggedInAsync();
        using var _ = h;

        Assert.True(await h.Db.ActivityEvents.AnyAsync(
            e => e.UserId == userId && e.Label == ActivityLabels.LoggedIn));
        Assert.False(await h.Db.ShiftSessions.AnyAsync());
    }
}
