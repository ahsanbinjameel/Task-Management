using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Quick Work is the one feature in the system that records work nobody approved, so the tests
/// that matter are the ones proving it is not a back door.
///
/// Three rules, each with its own way of going wrong: the one-thing-at-a-time rule must survive an
/// interruption that is not a task; promotion must produce a request and never a task, or approval
/// stops being what creates work; and a record with no outcome must not be allowed to count
/// towards somebody's day.
/// </summary>
public class QuickWorkTests
{
    private static readonly string[] WorkerRole = { "Worker" };

    private sealed record Fixture(TestHarness H, long WorkerId, long TaskId, string TaskNumber);

    /// <summary>A worker on shift, with an assigned task ready to be started.</summary>
    private static async Task<Fixture> WorkerWithTaskAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("rachel");
        var reviewer = await h.CreateUserAsync("victor");
        var worker = await h.CreateUserAsync("wu", roles: WorkerRole);

        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Month-end close is slow",
            Description = "The ledger export takes an hour.",
            Type = RequestType.Investigation,
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.Normal,
        });

        var task = await h.Db.Tasks.SingleAsync();
        await h.Assignment.AssignAsync(task.Id, reviewer.Id, new AssignTaskDto { AssigneeUserId = worker.Id });

        h.ActingAs(worker.Id, Permissions.TaskWork, Permissions.WorkforceTrackShift, Permissions.RequestCreate);
        await h.StartShiftAsync(worker.Id);

        return new Fixture(h, worker.Id, task.Id, task.TaskNumber);
    }

    [Fact]
    public async Task StartAsync_is_refused_when_the_person_is_not_on_shift()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        var worker = await h.CreateUserAsync("wu", roles: WorkerRole);
        h.ActingAs(worker.Id, Permissions.WorkforceTrackShift);

        var result = await h.QuickWork.StartAsync(worker.Id, new StartQuickWorkDto { Title = "Phone call" });

        Assert.False(result.IsSuccess);
        Assert.Equal("shift.not_open", result.Error!.Code);
    }

    [Fact]
    public async Task StartAsync_pauses_the_running_task_and_keeps_its_recorded_time()
    {
        var f = await WorkerWithTaskAsync();

        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromMinutes(50));

        var quick = await f.H.QuickWork.StartAsync(f.WorkerId,
            new StartQuickWorkDto { Title = "Kate called about the invoice run" });

        Assert.True(quick.IsSuccess);

        // The task is paused, not blocked: nothing is wrong with it, it simply waited.
        var task = await f.H.Db.Tasks.SingleAsync(t => t.Id == f.TaskId);
        Assert.Equal(WorkTaskStatus.Paused, task.Status);

        // Its session is closed and keeps the fifty minutes, flagged as an interruption.
        var session = await f.H.Db.WorkSessions.SingleAsync(s => s.TaskId == f.TaskId);
        Assert.Equal(WorkSessionStatus.Paused, session.Status);
        Assert.True(session.EndedByInterruption);
        Assert.Equal(TimeSpan.FromMinutes(50), session.SessionEnd!.Value - session.SessionStart);

        // InterruptedByTaskId means "displaced by that task". Nothing displaced it but a phone call.
        Assert.Null(session.InterruptedByTaskId);

        // And the quick work knows what to hand back to.
        Assert.Equal(f.TaskId, quick.Value!.InterruptedTaskId);
        Assert.Equal(f.TaskNumber, quick.Value.InterruptedTaskNumber);
    }

    [Fact]
    public async Task StartAsync_leaves_the_person_working_rather_than_idle()
    {
        var f = await WorkerWithTaskAsync();

        await f.H.QuickWork.StartAsync(f.WorkerId, new StartQuickWorkDto { Title = "Walk-up question" });

        var user = await f.H.Db.Users.SingleAsync(u => u.Id == f.WorkerId);
        Assert.Equal(WorkforceState.Working, user.WorkforceState);
    }

    [Fact]
    public async Task StartAsync_refuses_a_second_one()
    {
        var f = await WorkerWithTaskAsync();

        await f.H.QuickWork.StartAsync(f.WorkerId, new StartQuickWorkDto { Title = "First call" });
        var second = await f.H.QuickWork.StartAsync(f.WorkerId, new StartQuickWorkDto { Title = "Second call" });

        Assert.False(second.IsSuccess);
        Assert.Equal("quickwork.already_active", second.Error!.Code);
    }

    [Fact]
    public async Task FinishAsync_requires_an_outcome()
    {
        var f = await WorkerWithTaskAsync();
        var started = await f.H.QuickWork.StartAsync(f.WorkerId, new StartQuickWorkDto { Title = "Call" });

        var finished = await f.H.QuickWork.FinishAsync(
            started.Value!.Id, f.WorkerId, new FinishQuickWorkDto { Outcome = "   " });

        Assert.False(finished.IsSuccess);
        Assert.Equal("quickwork.outcome_required", finished.Error!.Code);
    }

    [Fact]
    public async Task FinishAsync_hands_the_interrupted_task_back()
    {
        var f = await WorkerWithTaskAsync();

        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        var started = await f.H.QuickWork.StartAsync(f.WorkerId, new StartQuickWorkDto { Title = "Call" });

        f.H.Clock.Advance(TimeSpan.FromMinutes(15));

        var finished = await f.H.QuickWork.FinishAsync(started.Value!.Id, f.WorkerId,
            new FinishQuickWorkDto { Outcome = "Explained the new export format.", ResumeInterruptedTask = true });

        Assert.True(finished.IsSuccess);
        Assert.Equal(TimeSpan.FromMinutes(15), finished.Value!.Duration);

        var task = await f.H.Db.Tasks.SingleAsync(t => t.Id == f.TaskId);
        Assert.Equal(WorkTaskStatus.InProgress, task.Status);

        // A fresh session, so the interrupted one keeps its own recorded time rather than being
        // reopened and swallowing the fifteen minutes that were not spent on the task.
        var sessions = await f.H.Db.WorkSessions.Where(s => s.TaskId == f.TaskId).ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, s => s.Status == WorkSessionStatus.Active);
    }

    [Fact]
    public async Task FinishAsync_can_leave_the_task_where_it_is()
    {
        var f = await WorkerWithTaskAsync();

        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        var started = await f.H.QuickWork.StartAsync(f.WorkerId, new StartQuickWorkDto { Title = "Call" });

        await f.H.QuickWork.FinishAsync(started.Value!.Id, f.WorkerId,
            new FinishQuickWorkDto { Outcome = "Handled.", ResumeInterruptedTask = false });

        var task = await f.H.Db.Tasks.SingleAsync(t => t.Id == f.TaskId);
        Assert.Equal(WorkTaskStatus.Paused, task.Status);
    }

    [Fact]
    public async Task PromoteAsync_raises_a_request_and_creates_no_task()
    {
        var f = await WorkerWithTaskAsync();
        var tasksBefore = await f.H.Db.Tasks.CountAsync();

        var started = await f.H.QuickWork.StartAsync(f.WorkerId,
            new StartQuickWorkDto { Title = "Kate wants a new report", ClientName = "Falcon Traders" });

        var promoted = await f.H.QuickWork.PromoteAsync(started.Value!.Id, f.WorkerId,
            new PromoteQuickWorkDto
            {
                Description = "A weekly summary of ledger exports, by client.",
                Type = RequestType.Report,
            });

        Assert.True(promoted.IsSuccess);
        Assert.NotNull(promoted.Value!.PromotedToRequestNumber);

        // The whole point: approval is still what creates work.
        Assert.Equal(tasksBefore, await f.H.Db.Tasks.CountAsync());

        var request = await f.H.Db.Requests.SingleAsync(r => r.Id == promoted.Value.PromotedToRequestId);
        Assert.Equal(RequestStatus.Submitted, request.Status);

        // Raised in the name of whoever took the call — the caller may have no account at all.
        Assert.Equal(f.WorkerId, request.RequestedByUserId);

        // The client came across without being retyped.
        Assert.NotNull(request.ClientId);
    }

    [Fact]
    public async Task PromoteAsync_refuses_a_second_request_from_the_same_record()
    {
        var f = await WorkerWithTaskAsync();
        var started = await f.H.QuickWork.StartAsync(f.WorkerId, new StartQuickWorkDto { Title = "Call" });

        await f.H.QuickWork.PromoteAsync(started.Value!.Id, f.WorkerId,
            new PromoteQuickWorkDto { Description = "First." });

        var again = await f.H.QuickWork.PromoteAsync(started.Value.Id, f.WorkerId,
            new PromoteQuickWorkDto { Description = "Second." });

        Assert.False(again.IsSuccess);
        Assert.Equal("quickwork.already_promoted", again.Error!.Code);
    }

    [Fact]
    public async Task Somebody_elses_record_cannot_be_finished()
    {
        var f = await WorkerWithTaskAsync();
        var other = await f.H.CreateUserAsync("morgan", roles: WorkerRole);

        var started = await f.H.QuickWork.StartAsync(f.WorkerId, new StartQuickWorkDto { Title = "Call" });

        var result = await f.H.QuickWork.FinishAsync(started.Value!.Id, other.Id,
            new FinishQuickWorkDto { Outcome = "Not mine to write." });

        Assert.False(result.IsSuccess);
        Assert.Equal("quickwork.not_owner", result.Error!.Code);
    }

    [Fact]
    public async Task Cancelled_quick_work_is_kept_but_does_not_count_towards_the_day()
    {
        var f = await WorkerWithTaskAsync();

        var counted = await f.H.QuickWork.StartAsync(f.WorkerId, new StartQuickWorkDto { Title = "Real call" });
        f.H.Clock.Advance(TimeSpan.FromMinutes(30));
        await f.H.QuickWork.FinishAsync(counted.Value!.Id, f.WorkerId,
            new FinishQuickWorkDto { Outcome = "Sorted.", ResumeInterruptedTask = false });

        var mistake = await f.H.QuickWork.StartAsync(f.WorkerId, new StartQuickWorkDto { Title = "Mis-click" });
        f.H.Clock.Advance(TimeSpan.FromMinutes(20));
        await f.H.QuickWork.CancelAsync(mistake.Value!.Id, f.WorkerId);

        var report = await f.H.Reports.DailyUserAsync(
            f.WorkerId, f.H.Calendar.ToBusinessDate(f.H.Clock.UtcNow));

        // Both rows are there — the mis-click is history, not something to hide.
        Assert.Equal(2, report.QuickWork.Count);
        Assert.Contains(report.QuickWork, q => q.WasCancelled);

        // Only the real thirty minutes are totalled.
        Assert.Equal(TimeSpan.FromMinutes(30), report.QuickWorkTime);
    }

    [Fact]
    public async Task The_daily_report_counts_the_interruption()
    {
        var f = await WorkerWithTaskAsync();

        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromMinutes(25));

        var started = await f.H.QuickWork.StartAsync(f.WorkerId,
            new StartQuickWorkDto { Title = "Urgent question from the floor" });

        f.H.Clock.Advance(TimeSpan.FromMinutes(10));
        await f.H.QuickWork.FinishAsync(started.Value!.Id, f.WorkerId,
            new FinishQuickWorkDto { Outcome = "Answered.", ResumeInterruptedTask = false });

        var report = await f.H.Reports.DailyUserAsync(
            f.WorkerId, f.H.Calendar.ToBusinessDate(f.H.Clock.UtcNow));

        Assert.Equal(1, report.Interruptions);
        Assert.Equal(TimeSpan.FromMinutes(10), report.QuickWorkTime);

        // The task keeps its own twenty-five minutes, separately.
        Assert.Contains(report.OwnedWork, w => w.TimeSpent == TimeSpan.FromMinutes(25));
    }
}
