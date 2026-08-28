using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// PRODUCT-CORE §6, invariant §4.13: once execution starts, committed scope does not silently grow.
///
/// The case these exist for is the one the plan calls the Faisal rule. Detail-report points arrive
/// on day one and the work is committed and scheduled around them; master-report points arrive on
/// day two. Neither available answer is right: punishing the requester for testing properly is
/// wrong, and quietly absorbing the new points into the running task is what blows the timeline
/// while making it look like the team was slow.
///
/// The answer is a third thing. Later rounds are cheap to raise and visible *as* later rounds: a
/// new request, its own number, its own triage decision, carrying the shared context so nobody
/// retypes it — and leaving the finish line of the running work exactly where it was.
/// </summary>
public class ScopeDisciplineTests
{
    private sealed record Fixture(TestHarness H, long RequestId, long RequesterId, long ReviewerId);

    private static async Task<Fixture> ApprovedRequestAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("faisal");
        var reviewer = await h.CreateUserAsync("ahsan");
        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Delivery order detail report total is wrong",
            Description = "The total row does not match the sum of the lines.",
            Type = RequestType.Bug,
            ClientName = "Impression Sourcing"
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.High,
            EstimatedEffortHours = 4m
        });

        return new Fixture(h, request.Value.Id, requester.Id, reviewer.Id);
    }

    [Fact]
    public async Task A_later_round_becomes_its_own_request_with_its_own_number()
    {
        var f = await ApprovedRequestAsync();
        using var _d = f.H;

        var followUp = await f.H.Requests.CreateFollowUpAsync(
            f.RequestId, f.RequesterId,
            new CreateFollowUpDto { Title = "Master report still shows the old total" });

        Assert.True(followUp.IsSuccess);

        var original = await f.H.Db.Requests.AsNoTracking().SingleAsync(r => r.Id == f.RequestId);
        Assert.NotEqual(original.RequestNumber, followUp.Value!.RequestNumber);
        Assert.Equal(RequestStatus.Submitted, followUp.Value.Status);
    }

    [Fact]
    public async Task It_is_recorded_as_a_later_round_and_linked_back()
    {
        var f = await ApprovedRequestAsync();
        using var _d = f.H;

        var second = await f.H.Requests.CreateFollowUpAsync(
            f.RequestId, f.RequesterId,
            new CreateFollowUpDto { Title = "Master report still shows the old total" });

        Assert.Equal(2, second.Value!.Round);
        Assert.Equal(f.RequestId, second.Value.RelatedRequestId);

        // And rounds keep counting, so a third pass reads as a third pass.
        var third = await f.H.Requests.CreateFollowUpAsync(
            second.Value.Id, f.RequesterId,
            new CreateFollowUpDto { Title = "And the print layout is off" });

        Assert.Equal(3, third.Value!.Round);
    }

    [Fact]
    public async Task The_shared_context_is_carried_over_so_nobody_retypes_it()
    {
        var f = await ApprovedRequestAsync();
        using var _d = f.H;

        var original = await f.H.Db.Requests.AsNoTracking().SingleAsync(r => r.Id == f.RequestId);

        var followUp = await f.H.Requests.CreateFollowUpAsync(
            f.RequestId, f.RequesterId,
            new CreateFollowUpDto { Title = "Master report still shows the old total" });

        var stored = await f.H.Db.Requests.AsNoTracking()
            .SingleAsync(r => r.Id == followUp.Value!.Id);

        Assert.Equal(original.ClientId, stored.ClientId);
        Assert.Equal(original.ModuleId, stored.ModuleId);
        Assert.Equal(original.FormId, stored.FormId);
        Assert.Equal(original.Type, stored.Type);
    }

    /// <summary>
    /// The whole point. The task committed on day one is untouched — same status, same estimate,
    /// same due date, and no second request attached to it.
    /// </summary>
    [Fact]
    public async Task Raising_one_does_not_touch_the_work_already_committed()
    {
        var f = await ApprovedRequestAsync();
        using var _d = f.H;

        var before = await f.H.Db.Tasks.AsNoTracking().SingleAsync();

        await f.H.Requests.CreateFollowUpAsync(
            f.RequestId, f.RequesterId,
            new CreateFollowUpDto { Title = "Master report still shows the old total" });

        var after = await f.H.Db.Tasks.AsNoTracking().SingleAsync();

        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.EstimatedEffortHours, after.EstimatedEffortHours);
        Assert.Equal(before.DueDate, after.DueDate);
        Assert.Equal(before.Title, after.Title);

        // Still exactly one task, and the new request has not been attached to it.
        Assert.Equal(1, await f.H.Db.Tasks.CountAsync());

        var followUp = await f.H.Db.Requests.AsNoTracking()
            .SingleAsync(r => r.RelatedRequestId == f.RequestId);
        Assert.Null(followUp.GeneratedTaskId);
    }

    /// <summary>
    /// The other half of §4.13, and the reason the follow-up has to exist: the edit path is closed
    /// once triage has acted, because the work has been planned around what the request says.
    /// </summary>
    [Fact]
    public async Task Editing_an_approved_request_is_refused_and_says_where_to_go()
    {
        var f = await ApprovedRequestAsync();
        using var _d = f.H;

        var result = await f.H.Requests.UpdateAsync(f.RequestId, f.RequesterId, new UpdateRequestDto
        {
            Title = "Detail report total AND master report total",
            Description = "Quietly adding the second thing to the first request.",
            Type = RequestType.Bug,
            RequestedUrgency = RequestedUrgency.High
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("request.not_editable", result.Error!.Code);
        Assert.Contains("follow-up", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A later round is a full request, so it goes through triage like everything else — and it
    /// only becomes work if a reviewer approves it. Approval is still the only thing that creates
    /// a task.
    /// </summary>
    [Fact]
    public async Task A_later_round_still_has_to_be_approved_before_it_is_work()
    {
        var f = await ApprovedRequestAsync();
        using var _d = f.H;

        var followUp = await f.H.Requests.CreateFollowUpAsync(
            f.RequestId, f.RequesterId,
            new CreateFollowUpDto { Title = "Master report still shows the old total" });

        Assert.Equal(1, await f.H.Db.Tasks.CountAsync());

        await f.H.Triage.DecideAsync(followUp.Value!.Id, f.ReviewerId, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.Normal
        });

        Assert.Equal(2, await f.H.Db.Tasks.CountAsync());
    }

    [Fact]
    public async Task A_follow_up_to_a_request_that_does_not_exist_is_a_not_found()
    {
        var f = await ApprovedRequestAsync();
        using var _d = f.H;

        var result = await f.H.Requests.CreateFollowUpAsync(
            999_999, f.RequesterId, new CreateFollowUpDto { Title = "Nothing to hang off" });

        Assert.Equal("request.not_found", result.Error!.Code);
    }

    /// <summary>
    /// Whoever finds it raises it. The person testing a fix is not always the person who reported
    /// the original, and making them go and ask would be the relay this product exists to remove.
    /// </summary>
    [Fact]
    public async Task Somebody_other_than_the_original_requester_may_raise_one()
    {
        var f = await ApprovedRequestAsync();
        using var _d = f.H;

        var colleague = await f.H.CreateUserAsync("hina");

        var followUp = await f.H.Requests.CreateFollowUpAsync(
            f.RequestId, colleague.Id,
            new CreateFollowUpDto { Title = "Found while checking the first fix" });

        Assert.True(followUp.IsSuccess);

        var stored = await f.H.Db.Requests.AsNoTracking()
            .SingleAsync(r => r.Id == followUp.Value!.Id);

        Assert.Equal(colleague.Id, stored.RequestedByUserId);
    }

    [Fact]
    public async Task An_ordinary_request_is_round_one()
    {
        var f = await ApprovedRequestAsync();
        using var _d = f.H;

        var original = await f.H.Db.Requests.AsNoTracking().SingleAsync(r => r.Id == f.RequestId);

        Assert.Equal(1, original.Round);
        Assert.Null(original.RelatedRequestId);
    }
}
