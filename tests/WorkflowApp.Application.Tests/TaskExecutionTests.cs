using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Phases 4-6: the workflow engine, assignment and the work timer, exercised through the real
/// services against an in-memory store.
/// </summary>
public class TaskExecutionTests
{
    private sealed record Fixture(TestHarness H, long TaskId, long WorkerId, long CoordinatorId);

    /// <summary>An approved request that has become a task, ready to be assigned.</summary>
    private static async Task<Fixture> ApprovedTaskAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("rachel");
        var reviewer = await h.CreateUserAsync("victor");
        var coordinator = await h.CreateUserAsync("amara");
        var worker = await h.CreateUserAsync("wu");

        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Proof-of-delivery photos missing from invoices",
            Description = "POD photos do not appear on the generated PDF.",
            Type = RequestType.Bug,
            RequestedUrgency = RequestedUrgency.High
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.High,
            EstimatedEffortHours = 6m
        });

        var task = await h.Db.Tasks.SingleAsync();
        return new Fixture(h, task.Id, worker.Id, coordinator.Id);
    }

    /// <summary>The same task, assigned to the worker and with their shift open.</summary>
    private static async Task<Fixture> AssignedAndOnShiftAsync()
    {
        var f = await ApprovedTaskAsync();
        f.H.ActingAsAdmin(f.CoordinatorId);

        await f.H.Assignment.AssignAsync(f.TaskId, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = f.WorkerId });

        await f.H.StartShiftAsync(f.WorkerId);
        f.H.ActingAsAdmin(f.WorkerId);
        return f;
    }

    // --- assignment ----------------------------------------------------------------------

    [Fact]
    public async Task Assigning_moves_the_task_to_assigned_and_records_history()
    {
        var f = await ApprovedTaskAsync();
        using var _d = f.H;
        f.H.ActingAsAdmin(f.CoordinatorId);

        var result = await f.H.Assignment.AssignAsync(f.TaskId, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = f.WorkerId });

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.Assigned, result.Value!.Status);
        Assert.Equal(f.WorkerId, result.Value.PrimaryAssigneeUserId);

        var history = await f.H.Db.AssignmentHistories.SingleAsync();
        Assert.Null(history.FromUserId);
        Assert.Equal(f.WorkerId, history.ToUserId);
        Assert.Equal(f.CoordinatorId, history.AssignedByUserId);

        // The status change is in the status trail too, not only the assignment trail.
        Assert.True(await f.H.Db.StatusHistories.AnyAsync(
            hst => hst.ToStatus == WorkTaskStatus.Assigned));
    }

    [Fact]
    public async Task Reassigning_requires_a_reason_and_keeps_the_earlier_record()
    {
        var f = await ApprovedTaskAsync();
        using var _d = f.H;
        f.H.ActingAsAdmin(f.CoordinatorId);
        var second = await f.H.CreateUserAsync("priya");

        await f.H.Assignment.AssignAsync(f.TaskId, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = f.WorkerId });

        var noReason = await f.H.Assignment.AssignAsync(f.TaskId, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = second.Id });
        Assert.Equal("task.reassign_reason_required", noReason.Error!.Code);

        var withReason = await f.H.Assignment.AssignAsync(f.TaskId, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = second.Id, Reason = "Wu is on leave" });
        Assert.True(withReason.IsSuccess);

        // Append-only: both assignments survive.
        var history = await f.H.Db.AssignmentHistories.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal(f.WorkerId, history[1].FromUserId);
        Assert.Equal(second.Id, history[1].ToUserId);
    }

    [Fact]
    public async Task A_deactivated_user_cannot_be_given_work()
    {
        var f = await ApprovedTaskAsync();
        using var _d = f.H;
        f.H.ActingAsAdmin(f.CoordinatorId);

        await f.H.UserAdmin.SetActiveAsync(f.WorkerId, false);

        var result = await f.H.Assignment.AssignAsync(f.TaskId, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = f.WorkerId });

        Assert.Equal("task.assignee_inactive", result.Error!.Code);
    }

    [Fact]
    public async Task New_work_goes_to_the_end_of_the_assignees_queue()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        f.H.ActingAsAdmin(f.CoordinatorId);

        // A second approved task for the same person.
        var requester = await f.H.Db.Users.FirstAsync(u => u.UserName == "rachel");
        var reviewer = await f.H.Db.Users.FirstAsync(u => u.UserName == "victor");
        var second = await f.H.Requests.CreateAsync(requester.Id, new CreateRequestDto
        { Title = "Second piece of work", Description = "…", Type = RequestType.Support });
        await f.H.Triage.DecideAsync(second.Value!.Id, reviewer.Id,
            new TriageDecisionDto { Outcome = TriageOutcome.Approve });

        var secondTask = await f.H.Db.Tasks.OrderBy(t => t.Id).LastAsync();
        await f.H.Assignment.AssignAsync(secondTask.Id, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = f.WorkerId });

        var queue = await f.H.TaskQueries.MyQueueAsync(f.WorkerId);
        Assert.Equal(new[] { f.TaskId, secondTask.Id }, queue.Select(t => t.Id));
    }

    [Fact]
    public async Task A_queue_can_be_reordered_but_only_with_the_owners_own_tasks()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;

        var stranger = await f.H.CreateUserAsync("stranger");
        var strangerTask = await f.H.Db.Tasks.FirstAsync();

        var bad = await f.H.Assignment.ReorderQueueAsync(stranger.Id, new[] { strangerTask.Id });
        Assert.Equal("queue.unknown_task", bad.Error!.Code);

        var good = await f.H.Assignment.ReorderQueueAsync(f.WorkerId, new[] { f.TaskId });
        Assert.True(good.IsSuccess);
    }

    [Fact]
    public async Task QC_cannot_be_the_person_who_did_the_work()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;

        var result = await f.H.Assignment.SetRolesAsync(f.TaskId,
            new SetTaskRolesDto { QCUserId = f.WorkerId });

        Assert.Equal("task.qc_cannot_be_assignee", result.Error!.Code);
    }

    // --- work sessions -------------------------------------------------------------------

    [Fact]
    public async Task Work_cannot_start_without_an_open_shift()
    {
        var f = await ApprovedTaskAsync();
        using var _d = f.H;
        f.H.ActingAsAdmin(f.CoordinatorId);
        await f.H.Assignment.AssignAsync(f.TaskId, f.CoordinatorId, new AssignTaskDto { AssigneeUserId = f.WorkerId });

        f.H.ActingAsAdmin(f.WorkerId);
        var result = await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        Assert.Equal("shift.not_open", result.Error!.Code);
    }

    [Fact]
    public async Task Only_the_assignee_can_start_the_timer()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        var other = await f.H.CreateUserAsync("interloper");
        await f.H.StartShiftAsync(other.Id);

        var result = await f.H.WorkSessions.StartAsync(f.TaskId, other.Id);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task Starting_work_opens_a_session_and_sets_the_user_to_working()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;

        var result = await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.InProgress, result.Value!.Status);

        var session = await f.H.Db.WorkSessions.SingleAsync();
        Assert.Equal(WorkSessionStatus.Active, session.Status);
        Assert.Null(session.SessionEnd);

        // Working is entered by starting a task — never claimed directly.
        var user = await f.H.Db.Users.FirstAsync(u => u.Id == f.WorkerId);
        Assert.Equal(WorkforceState.Working, user.WorkforceState);
    }

    [Fact]
    public async Task A_user_cannot_run_two_tasks_at_once()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        // A second assigned task for the same worker.
        var requester = await f.H.Db.Users.FirstAsync(u => u.UserName == "rachel");
        var reviewer = await f.H.Db.Users.FirstAsync(u => u.UserName == "victor");
        var second = await f.H.Requests.CreateAsync(requester.Id, new CreateRequestDto
        { Title = "Something else", Description = "…", Type = RequestType.Support });
        await f.H.Triage.DecideAsync(second.Value!.Id, reviewer.Id,
            new TriageDecisionDto { Outcome = TriageOutcome.Approve });
        var secondTask = await f.H.Db.Tasks.OrderBy(t => t.Id).LastAsync();
        await f.H.Assignment.AssignAsync(secondTask.Id, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = f.WorkerId });

        var result = await f.H.WorkSessions.StartAsync(secondTask.Id, f.WorkerId);

        Assert.Equal("worksession.already_active", result.Error!.Code);
        Assert.Equal(1, await f.H.Db.WorkSessions.CountAsync(s => s.Status == WorkSessionStatus.Active));
    }

    [Fact]
    public async Task Starting_the_task_that_is_already_running_is_a_no_op()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        var again = await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        Assert.True(again.IsSuccess);
        Assert.Equal(1, await f.H.Db.WorkSessions.CountAsync());
    }

    [Fact]
    public async Task Pausing_requires_a_reason_or_a_comment()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        var result = await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId, new StopWorkDto());

        Assert.Equal("worksession.reason_required", result.Error!.Code);
    }

    [Fact]
    public async Task A_pause_reason_that_demands_a_comment_is_enforced()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        f.H.Db.PauseReasons.Add(new Domain.Entities.Requests.PauseReason
        { Name = "Waiting for client", RequiresComment = true, IsBlocker = true });
        await f.H.Db.SaveChangesAsync();
        var reason = await f.H.Db.PauseReasons.FirstAsync();

        var withoutComment = await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId,
            new StopWorkDto { PauseReasonId = reason.Id });
        Assert.Equal("worksession.comment_required", withoutComment.Error!.Code);

        var withComment = await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId,
            new StopWorkDto { PauseReasonId = reason.Id, Comment = "Chased on Tuesday" });
        Assert.True(withComment.IsSuccess);
    }

    [Fact]
    public async Task Total_time_is_the_sum_of_sessions_not_start_to_finish()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;

        // Work 1h, break for 3h, work 30m. Elapsed is 4.5h; worked is 1.5h.
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromHours(1));
        await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId, new StopWorkDto { Comment = "Lunch" });

        f.H.Clock.Advance(TimeSpan.FromHours(3));
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromMinutes(30));
        await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId, new StopWorkDto { Comment = "End of day" });

        var detail = await f.H.TaskQueries.GetAsync(f.TaskId);
        Assert.Equal(TimeSpan.FromMinutes(90), detail.Value!.TotalWorkedTime);
        Assert.Equal(2, detail.Value.WorkSessions.Count);
    }

    [Fact]
    public async Task Pausing_releases_the_user_back_to_available()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        await f.H.WorkSessions.PauseAsync(f.TaskId, f.WorkerId, new StopWorkDto { Comment = "Stepping away" });

        var user = await f.H.Db.Users.FirstAsync(u => u.Id == f.WorkerId);
        // Still on shift, just not on a task.
        Assert.Equal(WorkforceState.Available, user.WorkforceState);
    }

    [Fact]
    public async Task Blocking_lands_in_blocked_not_paused()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        var result = await f.H.WorkSessions.BlockAsync(f.TaskId, f.WorkerId,
            new StopWorkDto { Comment = "Waiting on the payments team" });

        Assert.Equal(WorkTaskStatus.Blocked, result.Value!.Status);
    }

    [Fact]
    public async Task Completing_goes_to_QC_never_straight_to_closed()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromHours(2));

        var result = await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Fixed the PDF template.");

        Assert.Equal(WorkTaskStatus.CompletedReadyForQC, result.Value!.Status);
        Assert.NotEqual(WorkTaskStatus.Closed, result.Value.Status);
        Assert.Equal(100, result.Value.ProgressPercent);
        Assert.Equal(TimeSpan.FromHours(2), result.Value.TotalWorkedTime);

        // No session left running.
        Assert.False(await f.H.Db.WorkSessions.AnyAsync(s => s.Status == WorkSessionStatus.Active));
    }

    // --- interruption --------------------------------------------------------------------

    [Fact]
    public async Task Interrupting_preserves_the_original_session_and_leaves_it_resumable()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromHours(1));

        // An urgent second task for the same worker.
        var requester = await f.H.Db.Users.FirstAsync(u => u.UserName == "rachel");
        var reviewer = await f.H.Db.Users.FirstAsync(u => u.UserName == "victor");
        var urgent = await f.H.Requests.CreateAsync(requester.Id, new CreateRequestDto
        { Title = "Production outage", Description = "…", Type = RequestType.Bug });
        await f.H.Triage.DecideAsync(urgent.Value!.Id, reviewer.Id, new TriageDecisionDto
        { Outcome = TriageOutcome.Approve, ApprovedPriority = Priority.Critical });
        var urgentTask = await f.H.Db.Tasks.OrderBy(t => t.Id).LastAsync();
        await f.H.Assignment.AssignAsync(urgentTask.Id, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = f.WorkerId });

        var result = await f.H.WorkSessions.InterruptAsync(f.WorkerId,
            new InterruptDto { TaskId = urgentTask.Id, Comment = "Sev-1 escalation" });

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.InProgress, result.Value!.Status);

        // The interrupted task keeps its hour and is paused, not discarded.
        var original = await f.H.TaskQueries.GetAsync(f.TaskId);
        Assert.Equal(WorkTaskStatus.Paused, original.Value!.Status);
        Assert.Equal(TimeSpan.FromHours(1), original.Value.TotalWorkedTime);

        var pausedSession = await f.H.Db.WorkSessions.FirstAsync(s => s.TaskId == f.TaskId);
        Assert.True(pausedSession.EndedByInterruption);
        Assert.Equal(urgentTask.Id, pausedSession.InterruptedByTaskId);

        // The single-active rule survived the switch.
        Assert.Equal(1, await f.H.Db.WorkSessions.CountAsync(s => s.Status == WorkSessionStatus.Active));
    }

    [Fact]
    public async Task Interrupting_with_nothing_running_simply_starts_the_task()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;

        var result = await f.H.WorkSessions.InterruptAsync(f.WorkerId, new InterruptDto { TaskId = f.TaskId });

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.InProgress, result.Value!.Status);
    }

    // --- workflow engine -----------------------------------------------------------------

    [Fact]
    public async Task A_transition_outside_the_map_is_refused()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;

        var result = await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.WorkerId,
            new TransitionTaskDto { To = WorkTaskStatus.Closed });

        Assert.True(result.IsFailure);
        Assert.Equal("workflow.transition_not_allowed", result.Error!.Code);
    }

    [Fact]
    public async Task A_transition_without_the_permission_is_refused_as_forbidden()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;

        // Holds Task.Work but not Task.QCReview.
        f.H.ActingAs(f.WorkerId, Permissions.TaskWork);
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "done");

        var result = await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.WorkerId,
            new TransitionTaskDto { To = WorkTaskStatus.QCReview });

        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task A_reason_required_transition_is_refused_without_one()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        var result = await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.WorkerId,
            new TransitionTaskDto { To = WorkTaskStatus.Paused });

        Assert.True(result.IsFailure);
        Assert.Contains("reason", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Leaving_in_progress_closes_the_open_session()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromMinutes(45));

        await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.WorkerId,
            new TransitionTaskDto { To = WorkTaskStatus.Blocked, Reason = "Waiting on infrastructure" });

        // A transition must not strand a running timer.
        Assert.False(await f.H.Db.WorkSessions.AnyAsync(s => s.Status == WorkSessionStatus.Active));

        var detail = await f.H.TaskQueries.GetAsync(f.TaskId);
        Assert.Equal(TimeSpan.FromMinutes(45), detail.Value!.TotalWorkedTime);
    }

    [Fact]
    public async Task The_same_idempotency_key_applies_a_transition_once()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        var request = new TransitionTaskDto
        {
            To = WorkTaskStatus.Paused,
            Reason = "Double-clicked",
            IdempotencyKey = "abc-123"
        };

        await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.WorkerId, request);
        await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.WorkerId, request);
        await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.WorkerId, request);

        var pausedRows = await f.H.Db.StatusHistories
            .CountAsync(x => x.TaskId == f.TaskId && x.ToStatus == WorkTaskStatus.Paused);

        Assert.Equal(1, pausedRows);
    }

    [Fact]
    public async Task An_override_needs_the_permission_a_reason_and_is_audited()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;

        f.H.ActingAs(f.WorkerId, Permissions.TaskWork);
        var refused = await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.WorkerId,
            new TransitionTaskDto { To = WorkTaskStatus.Closed, Reason = "just because", IsOverride = true });
        Assert.Equal(ErrorType.Forbidden, refused.Error!.Type);

        f.H.ActingAs(f.WorkerId, Permissions.TaskWork, Permissions.TaskOverride);
        var noReason = await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.WorkerId,
            new TransitionTaskDto { To = WorkTaskStatus.Closed, IsOverride = true });
        Assert.True(noReason.IsFailure);

        var forced = await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.WorkerId,
            new TransitionTaskDto { To = WorkTaskStatus.Closed, Reason = "Customer withdrew the request", IsOverride = true });

        Assert.True(forced.IsSuccess);
        Assert.Equal(WorkTaskStatus.Closed, forced.Value!.Status);

        // Forcing is allowed, but never quiet.
        var history = await f.H.Db.StatusHistories.FirstAsync(x => x.ToStatus == WorkTaskStatus.Closed);
        Assert.True(history.WasOverride);
        Assert.True(await f.H.Db.AuditLogs.AnyAsync(
            a => a.Action == Common.Services.AuditActions.WorkflowOverride));
    }

    [Fact]
    public async Task Available_transitions_are_filtered_by_what_the_caller_may_do()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);

        var worker = f.H.TaskWorkflow.AvailableTransitions(
            WorkTaskStatus.CompletedReadyForQC, new HashSet<string> { Permissions.TaskWork });
        Assert.DoesNotContain(WorkTaskStatus.QCReview, worker);

        var qc = f.H.TaskWorkflow.AvailableTransitions(
            WorkTaskStatus.CompletedReadyForQC, new HashSet<string> { Permissions.TaskQCReview });
        Assert.Contains(WorkTaskStatus.QCReview, qc);
    }

    [Fact]
    public async Task The_whole_pipeline_runs_from_request_to_closed()
    {
        var f = await AssignedAndOnShiftAsync();
        using var _d = f.H;

        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromHours(3));
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Template corrected.");

        // QC and closure have their own services; the generic transition cannot reach those states.
        f.H.ActingAsAdmin(f.CoordinatorId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.CoordinatorId);
        await f.H.QC.SubmitAsync(f.TaskId, f.CoordinatorId,
            new SubmitQCReviewDto { Result = QCResult.Passed });

        var closed = await f.H.Closure.CloseAsync(f.TaskId, f.CoordinatorId, new CloseTaskDto());
        Assert.True(closed.IsSuccess, closed.Error?.Message);

        var final = await f.H.TaskQueries.GetAsync(f.TaskId);
        Assert.Equal(WorkTaskStatus.Closed, final.Value!.Status);
        Assert.Equal(TimeSpan.FromHours(3), final.Value.TotalWorkedTime);

        // A complete, gap-free audit trail from creation to closure.
        var trail = final.Value.StatusHistory.Select(x => x.ToStatus).ToList();
        Assert.Equal(WorkTaskStatus.ReadyForAssignment, trail.First());
        Assert.Equal(WorkTaskStatus.Closed, trail.Last());
        for (var i = 1; i < final.Value.StatusHistory.Count; i++)
        {
            Assert.Equal(final.Value.StatusHistory[i - 1].ToStatus, final.Value.StatusHistory[i].FromStatus);
        }
    }
}

public class NumberGeneratorTests
{
    [Fact]
    public async Task Numbers_are_padded_sequential_and_per_sequence()
    {
        using var h = new TestHarness();

        Assert.Equal("REQ-000001", await h.Numbers.NextAsync("Request", "REQ"));
        Assert.Equal("REQ-000002", await h.Numbers.NextAsync("Request", "REQ"));
        // A separate counter — tasks do not inherit the request numbering.
        Assert.Equal("TSK-000001", await h.Numbers.NextAsync("Task", "TSK"));
        Assert.Equal("REQ-000003", await h.Numbers.NextAsync("Request", "REQ"));
    }
}
