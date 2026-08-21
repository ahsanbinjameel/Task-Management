using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

public class WorkforceQueryServiceTests
{
    private const string Password = "CorrectHorse1";

    private static async Task<(TestHarness Harness, long UserId)> OnShiftAsync(
        TestHarness? existing = null, string userName = "worker1")
    {
        var h = existing ?? await new TestHarness().SeedRolesAndPermissionsAsync();
        var user = await h.CreateUserAsync(userName, roles: DefaultRoles.Worker);
        await h.Auth.LoginAsync(new LoginRequest { UserName = userName, Password = Password });
        await h.Shifts.StartShiftAsync(user.Id);
        return (h, user.Id);
    }

    [Fact]
    public async Task Active_workforce_is_empty_when_nobody_is_on_shift()
    {
        using var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        await h.CreateUserAsync();

        var active = await h.WorkforceQueries.GetActiveWorkforceAsync();

        Assert.Equal(0, active.TotalOnShift);
        Assert.Empty(active.Workers);
    }

    [Fact]
    public async Task Active_workforce_lists_only_users_with_an_open_shift()
    {
        var (h, onShiftUserId) = await OnShiftAsync();
        using var _ = h;

        // A second user who logged in but never started a shift.
        await h.CreateUserAsync("bystander");
        await h.Auth.LoginAsync(new LoginRequest { UserName = "bystander", Password = Password });

        var active = await h.WorkforceQueries.GetActiveWorkforceAsync();

        var worker = Assert.Single(active.Workers);
        Assert.Equal(onShiftUserId, worker.UserId);
        Assert.Equal(1, active.TotalOnShift);
        Assert.Equal(1, active.Available);
    }

    [Fact]
    public async Task Active_workforce_summarises_states_and_elapsed_time()
    {
        var (h, first) = await OnShiftAsync();
        using var _ = h;
        var (_, second) = await OnShiftAsync(h, "worker2");

        h.Clock.Advance(TimeSpan.FromHours(2));
        await h.Shifts.ChangeStateAsync(second, WorkforceState.Lunch, null);
        h.Clock.Advance(TimeSpan.FromMinutes(15));

        var active = await h.WorkforceQueries.GetActiveWorkforceAsync();

        Assert.Equal(2, active.TotalOnShift);
        Assert.Equal(1, active.Available);
        Assert.Equal(1, active.Away);
        Assert.Equal(0, active.Working);

        var atLunch = active.Workers.Single(w => w.UserId == second);
        Assert.Equal(WorkforceState.Lunch, atLunch.State);
        Assert.Equal(TimeSpan.FromMinutes(15), atLunch.TimeInState);
        Assert.Equal(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15), atLunch.ShiftDuration);

        Assert.Contains(active.Workers, w => w.UserId == first && w.State == WorkforceState.Available);
    }

    [Fact]
    public async Task Ending_a_shift_removes_the_user_from_the_active_view()
    {
        var (h, userId) = await OnShiftAsync();
        using var _ = h;

        await h.Shifts.EndShiftAsync(userId, null);

        var active = await h.WorkforceQueries.GetActiveWorkforceAsync();
        Assert.Empty(active.Workers);
    }

    [Fact]
    public async Task Daily_timeline_totals_a_full_day_of_activity()
    {
        // 09:00 start, 10:00 lunch, 10:30 back, 17:00 end.
        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var h = new TestHarness(start);
        using var _ = h;
        await h.SeedRolesAndPermissionsAsync();
        var (_, userId) = await OnShiftAsync(h);

        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.Shifts.ChangeStateAsync(userId, WorkforceState.Lunch, null);
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        await h.Shifts.ChangeStateAsync(userId, WorkforceState.Available, null);
        h.Clock.Advance(TimeSpan.FromHours(6.5));
        await h.Shifts.EndShiftAsync(userId, null);

        var timeline = await h.WorkforceQueries.GetDailyTimelineAsync(userId, new DateOnly(2026, 3, 10));

        Assert.True(timeline.IsSuccess);
        Assert.Equal(TimeSpan.FromHours(8), timeline.Value!.TotalOnShift);
        Assert.Equal(TimeSpan.FromMinutes(30), timeline.Value.TotalAway);
        // Nothing was actually worked on — Working only comes from starting a task.
        Assert.Equal(TimeSpan.Zero, timeline.Value.TotalProductive);
        Assert.Equal(TimeSpan.FromMinutes(30), timeline.Value.TimeByState[nameof(WorkforceState.Lunch)]);
    }

    [Fact]
    public async Task Daily_timeline_for_a_day_with_no_activity_is_empty_not_an_error()
    {
        using var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        var user = await h.CreateUserAsync();

        var timeline = await h.WorkforceQueries.GetDailyTimelineAsync(user.Id, new DateOnly(2020, 1, 1));

        Assert.True(timeline.IsSuccess);
        Assert.Empty(timeline.Value!.Entries);
        Assert.Equal(TimeSpan.Zero, timeline.Value.TotalOnShift);
    }

    [Fact]
    public async Task Daily_timeline_reports_not_found_for_an_unknown_user()
    {
        using var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var timeline = await h.WorkforceQueries.GetDailyTimelineAsync(9999, new DateOnly(2026, 3, 10));

        Assert.True(timeline.IsFailure);
        Assert.Equal(ErrorType.NotFound, timeline.Error!.Type);
    }

    [Fact]
    public async Task An_overnight_shift_carries_its_hours_into_the_next_day()
    {
        // Shift starts 22:00 on the 10th and is still running at 02:00 on the 11th.
        var start = new DateTimeOffset(2026, 3, 10, 22, 0, 0, TimeSpan.Zero);
        var h = new TestHarness(start);
        using var _ = h;
        await h.SeedRolesAndPermissionsAsync();
        var (_, userId) = await OnShiftAsync(h);

        h.Clock.Advance(TimeSpan.FromHours(4));   // now 02:00 on the 11th

        var firstDay = await h.WorkforceQueries.GetDailyTimelineAsync(userId, new DateOnly(2026, 3, 10));
        var secondDay = await h.WorkforceQueries.GetDailyTimelineAsync(userId, new DateOnly(2026, 3, 11));

        // 22:00 → midnight on day one ...
        Assert.Equal(TimeSpan.FromHours(2), firstDay.Value!.TotalOnShift);
        // ... and midnight → 02:00 on day two, carried across rather than lost.
        Assert.Equal(TimeSpan.FromHours(2), secondDay.Value!.TotalOnShift);
        Assert.Equal(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero), secondDay.Value.Entries[0].From);
    }

    [Fact]
    public async Task A_finished_shift_does_not_bleed_into_the_following_day()
    {
        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var h = new TestHarness(start);
        using var _ = h;
        await h.SeedRolesAndPermissionsAsync();
        var (_, userId) = await OnShiftAsync(h);

        h.Clock.Advance(TimeSpan.FromHours(8));
        await h.Shifts.EndShiftAsync(userId, null);
        h.Clock.Advance(TimeSpan.FromHours(12));   // into the next day

        var nextDay = await h.WorkforceQueries.GetDailyTimelineAsync(userId, new DateOnly(2026, 3, 11));

        Assert.Equal(TimeSpan.Zero, nextDay.Value!.TotalOnShift);
    }

    [Fact]
    public async Task Shift_history_is_paged_newest_first()
    {
        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var h = new TestHarness(start);
        using var _ = h;
        await h.SeedRolesAndPermissionsAsync();
        var (_, userId) = await OnShiftAsync(h);

        for (var i = 0; i < 3; i++)
        {
            h.Clock.Advance(TimeSpan.FromHours(4));
            await h.Shifts.EndShiftAsync(userId, null);
            h.Clock.Advance(TimeSpan.FromHours(4));
            await h.Shifts.StartShiftAsync(userId);
        }

        var history = await h.WorkforceQueries.GetShiftHistoryAsync(userId, null, null, new PageQuery { PageSize = 2 });

        Assert.True(history.IsSuccess);
        Assert.Equal(4, history.Value!.TotalCount);
        Assert.Equal(2, history.Value.Items.Count);
        Assert.True(history.Value.Items[0].ShiftStart > history.Value.Items[1].ShiftStart);
    }

    [Fact]
    public async Task Activity_returns_the_days_raw_events_in_order()
    {
        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var h = new TestHarness(start);
        using var _ = h;
        await h.SeedRolesAndPermissionsAsync();
        var (_, userId) = await OnShiftAsync(h);

        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.Shifts.ChangeStateAsync(userId, WorkforceState.Meeting, "Sprint planning");

        var activity = await h.WorkforceQueries.GetActivityAsync(userId, new DateOnly(2026, 3, 10));

        Assert.True(activity.IsSuccess);
        Assert.Equal(
            new[] { ActivityLabels.LoggedIn, "Shift Started", "Meeting Started" },
            activity.Value!.Select(e => e.Label));
        Assert.Equal("Sprint planning", activity.Value![^1].Note);
    }
}

public class ShiftMaintenanceServiceTests
{
    private const string Password = "CorrectHorse1";

    private static async Task<(TestHarness Harness, long UserId)> OnShiftAsync(DateTimeOffset start)
    {
        var h = new TestHarness(start);
        await h.SeedRolesAndPermissionsAsync();
        var user = await h.CreateUserAsync(roles: DefaultRoles.Worker);
        await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = Password });
        await h.Shifts.StartShiftAsync(user.Id);
        return (h, user.Id);
    }

    [Fact]
    public async Task A_shift_within_the_limit_is_left_alone()
    {
        var (h, _) = await OnShiftAsync(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        using var _ = h;

        h.Clock.Advance(TimeSpan.FromHours(10));   // under the 16h maximum

        Assert.Equal(0, await h.ShiftMaintenance.CloseStaleShiftsAsync());
        Assert.Null((await h.Db.ShiftSessions.SingleAsync()).ShiftEnd);
    }

    [Fact]
    public async Task An_abandoned_shift_is_closed_flagged_and_audited()
    {
        var (h, userId) = await OnShiftAsync(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        using var _ = h;

        h.Clock.Advance(TimeSpan.FromHours(20));   // past the 16h maximum

        var closed = await h.ShiftMaintenance.CloseStaleShiftsAsync();

        Assert.Equal(1, closed);

        var shift = await h.Db.ShiftSessions.SingleAsync();
        Assert.NotNull(shift.ShiftEnd);
        Assert.True(shift.EndedImproperly);
        // Nobody closed it — that is what distinguishes cleanup from a supervisor force-end.
        Assert.Null(shift.EndedByUserId);

        var user = await h.Db.Users.FirstAsync(u => u.Id == userId);
        Assert.Equal(WorkforceState.NotLoggedIn, user.WorkforceState);

        Assert.True(await h.Db.AuditLogs.AnyAsync(a => a.Action == AuditActions.ShiftAutoClosed));
        Assert.True(await h.Db.ActivityEvents.AnyAsync(e => e.Label == ActivityLabels.ShiftClosedAutomatically));
    }

    [Fact]
    public async Task An_abandoned_shift_ends_at_the_last_sign_of_life_not_at_sweep_time()
    {
        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var (h, userId) = await OnShiftAsync(start);
        using var _ = h;

        // Last thing they did was go to lunch at 13:00, then they vanished.
        h.Clock.Advance(TimeSpan.FromHours(4));
        var lastSeen = h.Clock.UtcNow;
        await h.Shifts.ChangeStateAsync(userId, WorkforceState.Lunch, null);

        h.Clock.Advance(TimeSpan.FromHours(20));
        await h.ShiftMaintenance.CloseStaleShiftsAsync();

        var shift = await h.Db.ShiftSessions.SingleAsync();
        // Crediting them until the sweep noticed would inflate attendance by 20 hours.
        Assert.Equal(lastSeen, shift.ShiftEnd);
    }

    [Fact]
    public async Task A_shift_with_no_events_after_it_started_ends_at_its_start()
    {
        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var h = new TestHarness(start);
        using var _ = h;
        await h.SeedRolesAndPermissionsAsync();
        var user = await h.CreateUserAsync();

        // A shift row with no activity events at all.
        h.Db.ShiftSessions.Add(new Domain.Entities.Workforce.ShiftSession
        {
            UserId = user.Id,
            ShiftStart = start
        });
        await h.Db.SaveChangesAsync();

        h.Clock.Advance(TimeSpan.FromHours(30));
        await h.ShiftMaintenance.CloseStaleShiftsAsync();

        var shift = await h.Db.ShiftSessions.SingleAsync();
        Assert.Equal(start, shift.ShiftEnd);
        Assert.True(shift.EndedImproperly);
    }

    [Fact]
    public async Task The_sweep_is_idempotent()
    {
        var (h, _) = await OnShiftAsync(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        using var _ = h;

        h.Clock.Advance(TimeSpan.FromHours(20));

        Assert.Equal(1, await h.ShiftMaintenance.CloseStaleShiftsAsync());
        Assert.Equal(0, await h.ShiftMaintenance.CloseStaleShiftsAsync());
        Assert.Equal(1, await h.Db.AuditLogs.CountAsync(a => a.Action == AuditActions.ShiftAutoClosed));
    }

    [Fact]
    public async Task After_cleanup_the_user_can_log_in_and_start_a_fresh_shift()
    {
        var (h, userId) = await OnShiftAsync(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        using var _ = h;

        h.Clock.Advance(TimeSpan.FromHours(20));
        await h.ShiftMaintenance.CloseStaleShiftsAsync();

        // The whole point: a stale shift must not lock them out of tomorrow.
        var login = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = Password });
        Assert.True(login.IsSuccess);

        var started = await h.Shifts.StartShiftAsync(userId);
        Assert.True(started.IsSuccess);
    }

    [Fact]
    public async Task The_sweep_closes_several_abandoned_shifts_at_once()
    {
        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var h = new TestHarness(start);
        using var _ = h;
        await h.SeedRolesAndPermissionsAsync();

        foreach (var name in new[] { "worker1", "worker2", "worker3" })
        {
            var user = await h.CreateUserAsync(name, roles: DefaultRoles.Worker);
            await h.Auth.LoginAsync(new LoginRequest { UserName = name, Password = Password });
            await h.Shifts.StartShiftAsync(user.Id);
        }

        h.Clock.Advance(TimeSpan.FromHours(20));

        Assert.Equal(3, await h.ShiftMaintenance.CloseStaleShiftsAsync());
        Assert.Equal(0, await h.Db.ShiftSessions.CountAsync(s => s.ShiftEnd == null));
    }
}

public class BusinessCalendarTests
{
    [Fact]
    public void An_unknown_time_zone_falls_back_to_utc_instead_of_crashing()
    {
        using var h = new TestHarness(timeZoneId: "Not/A_Real_Zone");

        // Reports shift by an offset, which is visible and fixable; a dead app is not.
        Assert.Equal(TimeZoneInfo.Utc, h.Calendar.TimeZone);
    }

    [Fact]
    public void Day_range_is_a_half_open_interval_of_exactly_one_day_in_utc()
    {
        using var h = new TestHarness();

        var (start, end) = h.Calendar.DayRange(new DateOnly(2026, 3, 10));

        Assert.Equal(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void Business_date_is_resolved_in_the_configured_zone_not_in_utc()
    {
        // 22:00 UTC on the 10th is already the 11th in a +05:00 zone.
        using var h = new TestHarness(timeZoneId: "Pakistan Standard Time");

        // Skip if the host lacks this zone (non-Windows CI) rather than fail for the wrong reason.
        if (h.Calendar.TimeZone == TimeZoneInfo.Utc) return;

        var instant = new DateTimeOffset(2026, 3, 10, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 3, 11), h.Calendar.ToBusinessDate(instant));
    }
}
