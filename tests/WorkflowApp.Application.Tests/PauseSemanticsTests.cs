using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;
using Xunit;

/// <summary>
/// A task's workflow state and a worker's activity state are two different things, and pausing
/// touches both — differently.
///
/// Going to lunch does not mean the work is stuck: the task is still claimed and continues when
/// the worker gets back, while the *person* is genuinely unavailable. Waiting on a client is the
/// mirror image: the task really cannot move, but the worker is free to pick something else up.
///
/// Deriving both from one flag is what made a paused-for-lunch task read as a stalled one, and
/// left the worker marked Available while they were away from their desk. These tests hold the two
/// axes apart.
/// </summary>
namespace WorkflowApp.Application.Tests;

public class PauseSemanticsTests
{
    private sealed record Fixture(TestHarness H, long TaskId, long SecondTaskId, long WorkerId);

    private static async Task<Fixture> WorkingWorkerAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("rachel");
        var reviewer = await h.CreateUserAsync("victor");
        var coordinator = await h.CreateUserAsync("amara");
        var worker = await h.CreateUserAsync("wu", roles: new[] { "Worker" });

        h.ActingAsAdmin(reviewer.Id);

        async Task<long> MakeTaskAsync(string title)
        {
            var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
            {
                Title = title, Description = title, Type = RequestType.Bug
            });
            await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
            {
                Outcome = TriageOutcome.Approve, ApprovedPriority = Priority.High
            });
            var created = await h.Db.Tasks.OrderByDescending(t => t.Id).FirstAsync();
            await h.Assignment.AssignAsync(created.Id, coordinator.Id,
                new AssignTaskDto { AssigneeUserId = worker.Id });
            return created.Id;
        }

        var first = await MakeTaskAsync("Invoice totals are wrong");
        var second = await MakeTaskAsync("Urgent: client cannot log in");

        await h.StartShiftAsync(worker.Id);
        await h.WorkSessions.StartAsync(first, worker.Id);

        return new Fixture(h, first, second, worker.Id);
    }

    private static async Task<long> ReasonIdAsync(TestHarness h, PauseCategory category) =>
        (await h.Db.PauseReasons.FirstAsync(p => p.Category == category)).Id;

    private static async Task<WorkforceState> StateAsync(TestHarness h, long userId) =>
        (await h.Db.Users.FirstAsync(u => u.Id == userId)).WorkforceState;

    [Fact]
    public async Task Going_to_lunch_does_not_make_the_task_blocked()
    {
        var f = await WorkingWorkerAsync();
        using var _d = f.H;

        var lunch = await ReasonIdAsync(f.H, PauseCategory.Lunch);
        var result = await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId,
            new StopWorkDto { PauseReasonId = lunch });

        Assert.True(result.IsSuccess);

        // The work is not stuck — it is still theirs and will carry on afterwards.
        Assert.Equal(WorkTaskStatus.Paused, result.Value!.Status);

        // The person, on the other hand, is genuinely away.
        Assert.Equal(WorkforceState.Lunch, await StateAsync(f.H, f.WorkerId));
    }

    [Fact]
    public async Task Waiting_for_a_client_blocks_the_task_but_leaves_the_worker_free()
    {
        var f = await WorkingWorkerAsync();
        using var _d = f.H;

        var waiting = await ReasonIdAsync(f.H, PauseCategory.WaitingForClient);
        var result = await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId,
            new StopWorkDto { PauseReasonId = waiting, Comment = "Needs yesterday's backup." });

        Assert.True(result.IsSuccess);

        // This one really cannot move on...
        Assert.Equal(WorkTaskStatus.Blocked, result.Value!.Status);

        // ...but the worker is still at their desk and can pick up something else.
        Assert.Equal(WorkforceState.Available, await StateAsync(f.H, f.WorkerId));
    }

    [Fact]
    public async Task Time_away_is_recorded_so_the_day_does_not_read_as_productive()
    {
        var f = await WorkingWorkerAsync();
        using var _d = f.H;

        var lunch = await ReasonIdAsync(f.H, PauseCategory.Lunch);
        await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId,
            new StopWorkDto { PauseReasonId = lunch, Comment = "Back at 2." });

        // Without an activity event the timeline would show the break as working time.
        var events = await f.H.Db.ActivityEvents
            .Where(e => e.UserId == f.WorkerId && e.ResultingState == WorkforceState.Lunch)
            .ToListAsync();

        var recorded = Assert.Single(events);
        Assert.Equal(f.TaskId, recorded.RelatedTaskId);
        Assert.Equal("Back at 2.", recorded.Note);
    }

    [Fact]
    public async Task The_free_text_detail_is_kept_with_the_session_for_reporting()
    {
        var f = await WorkingWorkerAsync();
        using var _d = f.H;

        var urgent = await ReasonIdAsync(f.H, PauseCategory.OtherWorkUrgent);
        await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId, new StopWorkDto
        {
            PauseReasonId = urgent,
            Comment = "Salman asked me to check the client's live stock issue through AnyDesk."
        });

        var session = await f.H.Db.WorkSessions
            .Where(s => s.TaskId == f.TaskId)
            .OrderByDescending(s => s.Id)
            .FirstAsync();

        // Category for reporting, free text for what actually happened. Both survive.
        Assert.Equal(urgent, session.EndPauseReasonId);
        Assert.Contains("AnyDesk", session.EndComment);
    }

    [Fact]
    public async Task An_interruption_pauses_the_previous_task_and_keeps_the_worker_working()
    {
        var f = await WorkingWorkerAsync();
        using var _d = f.H;

        var urgent = await ReasonIdAsync(f.H, PauseCategory.OtherWorkUrgent);

        var switched = await f.H.WorkSessions.InterruptAsync(f.WorkerId, new InterruptDto
        {
            TaskId = f.SecondTaskId,
            PauseReasonId = urgent,
            Comment = "Client cannot log in."
        });

        Assert.True(switched.IsSuccess);
        Assert.Equal(WorkTaskStatus.InProgress, switched.Value!.Status);

        // The interrupted task is paused, never blocked: nothing is wrong with it, it just waited.
        var previous = await f.H.Db.Tasks.FirstAsync(t => t.Id == f.TaskId);
        Assert.Equal(WorkTaskStatus.Paused, previous.Status);

        // Its time is preserved and the switch is traceable, so it can be resumed later.
        var paused = await f.H.Db.WorkSessions
            .Where(s => s.TaskId == f.TaskId).OrderByDescending(s => s.Id).FirstAsync();
        Assert.Equal(WorkSessionStatus.Paused, paused.Status);
        Assert.True(paused.EndedByInterruption);
        Assert.Equal(f.SecondTaskId, paused.InterruptedByTaskId);
        Assert.NotNull(paused.SessionEnd);

        // The worker never stopped working — they are on the other task now.
        Assert.Equal(WorkforceState.Working, await StateAsync(f.H, f.WorkerId));
    }

    [Fact]
    public async Task The_previous_task_can_be_resumed_after_the_interruption()
    {
        var f = await WorkingWorkerAsync();
        using var _d = f.H;

        var urgent = await ReasonIdAsync(f.H, PauseCategory.OtherWorkUrgent);
        await f.H.WorkSessions.InterruptAsync(f.WorkerId,
            new InterruptDto { TaskId = f.SecondTaskId, PauseReasonId = urgent, Comment = "urgent" });

        f.H.Clock.Advance(TimeSpan.FromMinutes(20));

        // Coming back is an interruption in the other direction; the rule is the same.
        var back = await f.H.WorkSessions.InterruptAsync(f.WorkerId,
            new InterruptDto { TaskId = f.TaskId, PauseReasonId = urgent, Comment = "done, back to it" });

        Assert.True(back.IsSuccess);
        Assert.Equal(WorkTaskStatus.InProgress, back.Value!.Status);

        // Both stretches of work on the first task survive as separate sessions.
        var sessions = await f.H.Db.WorkSessions.Where(s => s.TaskId == f.TaskId).ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, s => s.Status == WorkSessionStatus.Active);
    }
}
