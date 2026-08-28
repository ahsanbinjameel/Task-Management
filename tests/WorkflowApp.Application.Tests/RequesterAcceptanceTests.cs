using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// PRODUCT-CORE §7: internal correctness is not client acceptance.
///
/// Passing our own quality check means the work is right by our lights. It does not mean the person
/// who asked has seen it working on their own instance, and until this existed the difference was
/// closed by a WhatsApp message and somebody updating a sheet — the last hop of exactly the relay
/// the product is meant to remove.
///
/// The through-line here is that the confirmation belongs to the requester and to nobody else, that
/// saying "still not fixed" costs the work a fresh quality check, and that work with no requester
/// behind it is never left waiting for a confirmation that cannot arrive.
/// </summary>
public class RequesterAcceptanceTests
{
    private const string Criteria =
        "- The delivery order total is correct\n- Existing orders are unaffected";

    private sealed record Fixture(
        TestHarness H, long TaskId, long RequesterId, long WorkerId, long QCUserId, long CoordinatorId);

    /// <summary>A task that has passed QC and is waiting on the person who asked for it.</summary>
    private static async Task<Fixture> AwaitingConfirmationAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("faisal");
        var reviewer = await h.CreateUserAsync("ahsan");
        var worker = await h.CreateUserAsync("hanzala");
        var qc = await h.CreateUserAsync("uzair");

        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Delivery Order detail report total is wrong",
            Description = "The total row does not match the sum of the lines.",
            Type = RequestType.Bug,
            RequestedUrgency = RequestedUrgency.High
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.High,
            AcceptanceCriteria = Criteria
        });

        var task = await h.Db.Tasks.SingleAsync();

        await h.Assignment.AssignAsync(task.Id, reviewer.Id, new AssignTaskDto { AssigneeUserId = worker.Id });

        await h.StartShiftAsync(worker.Id);
        h.ActingAsAdmin(worker.Id);
        await h.WorkSessions.StartAsync(task.Id, worker.Id);
        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.WorkSessions.CompleteAsync(task.Id, worker.Id, "Corrected the total expression.");

        h.ActingAsAdmin(qc.Id);
        await h.QC.StartReviewAsync(task.Id, qc.Id);
        await h.QC.SubmitAsync(task.Id, qc.Id, new SubmitQCReviewDto
        {
            Result = QCResult.Passed,
            Criteria = new[]
            {
                new AcceptanceCriterionVerdictDto { Index = 0, Met = true },
                new AcceptanceCriterionVerdictDto { Index = 1, Met = true },
            }
        });

        return new Fixture(h, task.Id, requester.Id, worker.Id, qc.Id, reviewer.Id);
    }

    [Fact]
    public async Task Requester_confirming_the_fix_closes_the_work()
    {
        var f = await AwaitingConfirmationAsync();
        using var _d = f.H;

        var result = await f.H.Closure.AcceptAsync(
            f.TaskId, f.RequesterId,
            new AcceptFixDto { Note = "Checked on our instance, the total is right now." });

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.Closed, result.Value!.Status);
    }

    [Fact]
    public async Task Confirming_records_the_requesters_own_words_as_the_resolution()
    {
        var f = await AwaitingConfirmationAsync();
        using var _d = f.H;

        // Nobody wrote a resolution, which is the ordinary case: the worker described what they did
        // in the session note rather than in the closure field.
        var open = await f.H.Db.Tasks.SingleAsync();
        open.Resolution = null;
        await f.H.Db.SaveChangesAsync();

        await f.H.Closure.AcceptAsync(
            f.TaskId, f.RequesterId, new AcceptFixDto { Note = "Total matches now." });

        var closed = await f.H.Db.Tasks.AsNoTracking().SingleAsync();

        Assert.Contains("requester", closed.Resolution!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Total matches now.", closed.Resolution!);
    }

    [Fact]
    public async Task Only_the_person_who_asked_may_confirm_the_fix()
    {
        var f = await AwaitingConfirmationAsync();
        using var _d = f.H;

        // The coordinator holds every permission there is. That is deliberately not enough here:
        // whether the thing is actually fixed is not a question authority can answer.
        var coordinator = await f.H.Closure.AcceptAsync(f.TaskId, f.CoordinatorId, new AcceptFixDto());
        Assert.Equal("acceptance.not_requester", coordinator.Error!.Code);

        var checker = await f.H.Closure.AcceptAsync(f.TaskId, f.QCUserId, new AcceptFixDto());
        Assert.Equal("acceptance.not_requester", checker.Error!.Code);

        var worker = await f.H.Closure.RejectAsync(
            f.TaskId, f.WorkerId, new RejectFixDto { Reason = "I think it is fine." });
        Assert.Equal("acceptance.not_requester", worker.Error!.Code);
    }

    [Fact]
    public async Task Requester_rejecting_sends_the_work_back_rather_than_closing_it()
    {
        var f = await AwaitingConfirmationAsync();
        using var _d = f.H;

        var result = await f.H.Closure.RejectAsync(f.TaskId, f.RequesterId, new RejectFixDto
        {
            Reason = "The detail report is right but the master report still shows the old total."
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.Reopened, result.Value!.Status);
    }

    [Fact]
    public async Task A_rejected_fix_cannot_close_again_on_the_quality_check_it_already_had()
    {
        var f = await AwaitingConfirmationAsync();
        using var _d = f.H;

        // A requester checks the fix on their own instance, so the rejection is always some way
        // after the quality check. Said explicitly because the staleness test compares timestamps:
        // on a fixed clock the two would otherwise land on the same tick.
        f.H.Clock.Advance(TimeSpan.FromHours(3));

        await f.H.Closure.RejectAsync(f.TaskId, f.RequesterId, new RejectFixDto
        {
            Reason = "Still showing the old total."
        });

        // The pass recorded before the rejection says nothing about the work done since, so the
        // closure gate has to refuse it. This falls out of the existing reopen rule rather than
        // from anything acceptance added — which is the point of landing in Reopened.
        var checklist = await f.H.Closure.EvaluateAsync(f.TaskId);
        var qc = checklist.Value!.Requirements.Single(r => r.Code == "closure.qc_passed");

        Assert.False(qc.IsMet);
    }

    [Fact]
    public async Task Saying_it_is_still_not_fixed_requires_saying_what_is_wrong()
    {
        var f = await AwaitingConfirmationAsync();
        using var _d = f.H;

        var result = await f.H.Closure.RejectAsync(
            f.TaskId, f.RequesterId, new RejectFixDto { Reason = "   " });

        Assert.Equal("acceptance.reason_required", result.Error!.Code);
    }

    [Fact]
    public async Task There_is_nothing_to_confirm_until_the_quality_check_has_passed()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        using var _d = h;

        var requester = await h.CreateUserAsync("faisal");
        var reviewer = await h.CreateUserAsync("ahsan");
        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Invoice print is cut off",
            Description = "The right-hand column runs off the page.",
            Type = RequestType.Bug
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.Normal
        });

        var task = await h.Db.Tasks.SingleAsync();

        var result = await h.Closure.AcceptAsync(task.Id, requester.Id, new AcceptFixDto());

        Assert.Equal("acceptance.not_awaiting_confirmation", result.Error!.Code);
    }

    [Fact]
    public async Task Confirming_twice_is_the_same_as_confirming_once()
    {
        var f = await AwaitingConfirmationAsync();
        using var _d = f.H;

        await f.H.Closure.AcceptAsync(f.TaskId, f.RequesterId, new AcceptFixDto());
        var again = await f.H.Closure.AcceptAsync(f.TaskId, f.RequesterId, new AcceptFixDto());

        Assert.True(again.IsSuccess);
        Assert.Equal(WorkTaskStatus.Closed, again.Value!.Status);
    }

    [Fact]
    public async Task The_closure_checklist_names_the_person_who_has_to_confirm()
    {
        var f = await AwaitingConfirmationAsync();
        using var _d = f.H;

        var checklist = await f.H.Closure.EvaluateAsync(f.TaskId);

        Assert.True(checklist.Value!.RequiresRequesterAcceptance);
        Assert.Equal("faisal", checklist.Value.RequesterDisplayName);
        Assert.False(checklist.Value.RequesterHasConfirmed);
    }

    /// <summary>
    /// The policy half of PRODUCT-CORE §4.14. Acceptance is not a universal invariant: work with
    /// nobody behind it must not be left waiting for a confirmation that can never come.
    /// </summary>
    [Fact]
    public async Task Work_with_no_requester_behind_it_needs_no_confirmation()
    {
        var f = await AwaitingConfirmationAsync();
        using var _d = f.H;

        var task = await f.H.Db.Tasks.SingleAsync();
        task.RequestId = null;
        await f.H.Db.SaveChangesAsync();

        var checklist = await f.H.Closure.EvaluateAsync(f.TaskId);
        Assert.False(checklist.Value!.RequiresRequesterAcceptance);
        Assert.Null(checklist.Value.RequesterDisplayName);

        // And the ordinary closure path still works, unchanged.
        var closed = await f.H.Closure.CloseAsync(
            f.TaskId, f.CoordinatorId, new CloseTaskDto { Resolution = "Done internally." });

        Assert.True(closed.IsSuccess);
        Assert.Equal(WorkTaskStatus.Closed, closed.Value!.Status);
    }

    /// <summary>
    /// A coordinator is told, not blocked. Requester acceptance is the normal route rather than a
    /// hard gate — a requester who goes quiet must not be able to strand finished work, and forcing
    /// that through the override path would make an ordinary Tuesday look like an incident.
    /// </summary>
    [Fact]
    public async Task A_coordinator_can_still_close_work_the_requester_has_not_confirmed()
    {
        var f = await AwaitingConfirmationAsync();
        using var _d = f.H;

        var checklist = await f.H.Closure.EvaluateAsync(f.TaskId);
        Assert.True(checklist.Value!.RequiresRequesterAcceptance);
        Assert.True(checklist.Value.IsReady);

        var closed = await f.H.Closure.CloseAsync(
            f.TaskId, f.CoordinatorId,
            new CloseTaskDto { Resolution = "Requester unreachable for a week." });

        Assert.True(closed.IsSuccess);
    }
}
