using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Requests.Services;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Phase 3 + 4: intake, triage, and the request→task boundary. The rule under test throughout is
/// that a request never becomes work on its own.
/// </summary>
public class RequestPipelineTests
{
    private static async Task<(TestHarness H, long RequesterId, long ReviewerId)> ReadyAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        var requester = await h.CreateUserAsync("rachel");
        var reviewer = await h.CreateUserAsync("victor");
        h.ActingAsAdmin(reviewer.Id);
        return (h, requester.Id, reviewer.Id);
    }

    private static CreateRequestDto NewRequest(string title = "Labels print the wrong depot code") => new()
    {
        Title = title,
        Description = "Printed labels show the origin depot instead of the destination.",
        Type = RequestType.Bug,
        RequestedUrgency = RequestedUrgency.High
    };

    [Fact]
    public async Task Submitting_a_request_creates_no_task_and_no_queue_entry()
    {
        var (h, requester, _) = await ReadyAsync();
        using var _d = h;

        var result = await h.Requests.CreateAsync(requester, NewRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(RequestStatus.Submitted, result.Value!.Status);
        Assert.Null(result.Value.GeneratedTaskId);
        // The whole point of separating requests from tasks.
        Assert.False(await h.Db.Tasks.AnyAsync());
    }

    [Fact]
    public async Task Request_numbers_are_sequential_and_unique()
    {
        var (h, requester, _) = await ReadyAsync();
        using var _d = h;

        var first = await h.Requests.CreateAsync(requester, NewRequest("One"));
        var second = await h.Requests.CreateAsync(requester, NewRequest("Two"));
        var third = await h.Requests.CreateAsync(requester, NewRequest("Three"));

        Assert.Equal("REQ-000001", first.Value!.RequestNumber);
        Assert.Equal("REQ-000002", second.Value!.RequestNumber);
        Assert.Equal("REQ-000003", third.Value!.RequestNumber);
    }

    [Fact]
    public async Task Only_the_requester_can_edit_and_only_before_triage_acts()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var created = await h.Requests.CreateAsync(requester, NewRequest());
        var id = created.Value!.Id;

        var update = new UpdateRequestDto
        {
            Title = "Updated title", Description = "Updated description",
            Type = RequestType.Bug, RequestedUrgency = RequestedUrgency.Critical
        };

        var byStranger = await h.Requests.UpdateAsync(id, reviewer, update);
        Assert.Equal(ErrorType.Forbidden, byStranger.Error!.Type);

        var byOwner = await h.Requests.UpdateAsync(id, requester, update);
        Assert.True(byOwner.IsSuccess);

        // Once approved, editing would invalidate the decision that was made.
        await h.Triage.DecideAsync(id, reviewer, new TriageDecisionDto { Outcome = TriageOutcome.Approve });

        var afterApproval = await h.Requests.UpdateAsync(id, requester, update);
        Assert.Equal("request.not_editable", afterApproval.Error!.Code);
    }

    [Fact]
    public async Task Approval_is_the_only_outcome_that_creates_a_task()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var outcomes = new (TriageOutcome Outcome, string? Reason, RequestStatus Expected)[]
        {
            (TriageOutcome.Reject, "Not a defect — working as designed", RequestStatus.Rejected),
            (TriageOutcome.Defer, "Revisit next quarter", RequestStatus.Deferred),
            (TriageOutcome.Escalate, "Needs a management decision", RequestStatus.Escalated)
        };

        foreach (var (outcome, reason, expected) in outcomes)
        {
            var created = await h.Requests.CreateAsync(requester, NewRequest($"{outcome}"));
            var result = await h.Triage.DecideAsync(created.Value!.Id, reviewer,
                new TriageDecisionDto { Outcome = outcome, Reason = reason });

            Assert.True(result.IsSuccess);
            Assert.Equal(expected, result.Value!.Status);
            Assert.Null(result.Value.CreatedTaskId);
        }

        // None of the three produced executable work.
        Assert.False(await h.Db.Tasks.AnyAsync());
    }

    [Fact]
    public async Task Rejecting_deferring_and_duplicating_all_require_a_reason()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        foreach (var outcome in new[]
                 {
                     TriageOutcome.Reject, TriageOutcome.Defer,
                     TriageOutcome.MarkDuplicate, TriageOutcome.RequestClarification
                 })
        {
            var created = await h.Requests.CreateAsync(requester, NewRequest($"{outcome}"));
            var result = await h.Triage.DecideAsync(created.Value!.Id, reviewer,
                new TriageDecisionDto { Outcome = outcome });

            Assert.True(result.IsFailure);
            Assert.Equal("triage.reason_required", result.Error!.Code);
        }
    }

    [Fact]
    public async Task Approving_creates_a_task_that_points_back_at_the_request()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var created = await h.Requests.CreateAsync(requester, NewRequest());

        var result = await h.Triage.DecideAsync(created.Value!.Id, reviewer, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.Critical,
            EstimatedEffortHours = 8m
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("TSK-000001", result.Value!.CreatedTaskNumber);

        var task = await h.Db.Tasks.SingleAsync();
        Assert.Equal(created.Value.Id, task.RequestId);          // provenance
        Assert.Equal(Priority.Critical, task.Priority);
        Assert.Equal(8m, task.EstimatedEffortHours);
        // Born schedulable, with nobody on it.
        Assert.Equal(WorkTaskStatus.ReadyForAssignment, task.Status);
        Assert.Null(task.PrimaryAssigneeUserId);

        var request = await h.Db.Requests.SingleAsync(r => r.Id == created.Value.Id);
        Assert.Equal(task.Id, request.GeneratedTaskId);
    }

    [Fact]
    public async Task Requested_urgency_is_advisory_and_the_approved_priority_wins()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        // Requester asks for Critical; the reviewer decides it is Low.
        var created = await h.Requests.CreateAsync(requester, NewRequest() with
        {
            RequestedUrgency = RequestedUrgency.Critical
        });

        await h.Triage.DecideAsync(created.Value!.Id, reviewer, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.Low
        });

        var task = await h.Db.Tasks.SingleAsync();
        Assert.Equal(Priority.Low, task.Priority);
    }

    [Fact]
    public async Task Urgency_maps_to_a_default_priority_when_the_reviewer_does_not_set_one()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var created = await h.Requests.CreateAsync(requester, NewRequest() with
        {
            RequestedUrgency = RequestedUrgency.Critical
        });

        await h.Triage.DecideAsync(created.Value!.Id, reviewer,
            new TriageDecisionDto { Outcome = TriageOutcome.Approve });

        Assert.Equal(Priority.Critical, (await h.Db.Tasks.SingleAsync()).Priority);
    }

    [Fact]
    public async Task Clarification_sends_the_request_back_and_answering_returns_it_to_review()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var created = await h.Requests.CreateAsync(requester, NewRequest());
        var id = created.Value!.Id;

        var asked = await h.Triage.DecideAsync(id, reviewer, new TriageDecisionDto
        {
            Outcome = TriageOutcome.RequestClarification,
            Reason = "Which depot codes are affected?"
        });
        Assert.Equal(RequestStatus.ClarificationRequired, asked.Value!.Status);

        var clarificationId = (await h.Db.RequestClarifications.SingleAsync()).Id;
        var answered = await h.Triage.AnswerClarificationAsync(clarificationId, requester, "All of them.");

        // Back to the review queue — never straight to approved.
        Assert.Equal(RequestStatus.Submitted, answered.Value!.Status);
        Assert.NotEqual(RequestStatus.Approved, answered.Value.Status);
    }

    [Fact]
    public async Task Clarification_history_is_append_only()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var created = await h.Requests.CreateAsync(requester, NewRequest());
        var id = created.Value!.Id;

        await h.Triage.DecideAsync(id, reviewer, new TriageDecisionDto
        { Outcome = TriageOutcome.RequestClarification, Reason = "First question?" });

        var first = (await h.Db.RequestClarifications.SingleAsync()).Id;
        await h.Triage.AnswerClarificationAsync(first, requester, "First answer.");

        await h.Triage.DecideAsync(id, reviewer, new TriageDecisionDto
        { Outcome = TriageOutcome.RequestClarification, Reason = "Second question?" });

        // The first exchange survives the second round.
        var thread = await h.Db.RequestClarifications.OrderBy(c => c.Id).ToListAsync();
        Assert.Equal(2, thread.Count);
        Assert.Equal("First answer.", thread[0].Answer);
        Assert.Null(thread[1].Answer);
    }

    [Fact]
    public async Task An_open_clarification_blocks_approval()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var created = await h.Requests.CreateAsync(requester, NewRequest());
        await h.Triage.DecideAsync(created.Value!.Id, reviewer, new TriageDecisionDto
        { Outcome = TriageOutcome.RequestClarification, Reason = "Need detail" });

        var result = await h.Triage.DecideAsync(created.Value.Id, reviewer,
            new TriageDecisionDto { Outcome = TriageOutcome.Approve });

        Assert.True(result.IsFailure);
        Assert.Equal("request.clarification_pending", result.Error!.Code);
        Assert.False(await h.Db.Tasks.AnyAsync());
    }

    [Fact]
    public async Task Only_the_requester_can_answer_their_clarification()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var created = await h.Requests.CreateAsync(requester, NewRequest());
        await h.Triage.DecideAsync(created.Value!.Id, reviewer, new TriageDecisionDto
        { Outcome = TriageOutcome.RequestClarification, Reason = "Need detail" });

        var clarificationId = (await h.Db.RequestClarifications.SingleAsync()).Id;

        var byReviewer = await h.Triage.AnswerClarificationAsync(clarificationId, reviewer, "I'll answer my own question");
        Assert.Equal(ErrorType.Forbidden, byReviewer.Error!.Type);
    }

    [Fact]
    public async Task A_clarification_cannot_be_answered_twice()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var created = await h.Requests.CreateAsync(requester, NewRequest());
        await h.Triage.DecideAsync(created.Value!.Id, reviewer, new TriageDecisionDto
        { Outcome = TriageOutcome.RequestClarification, Reason = "Need detail" });

        var clarificationId = (await h.Db.RequestClarifications.SingleAsync()).Id;
        await h.Triage.AnswerClarificationAsync(clarificationId, requester, "Here you go.");

        var second = await h.Triage.AnswerClarificationAsync(clarificationId, requester, "Changed my mind.");
        Assert.Equal("clarification.already_answered", second.Error!.Code);
    }

    [Fact]
    public async Task Marking_a_duplicate_links_the_original_and_creates_no_task()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var original = await h.Requests.CreateAsync(requester, NewRequest("Original"));
        var copy = await h.Requests.CreateAsync(requester, NewRequest("Same thing again"));

        var result = await h.Triage.DecideAsync(copy.Value!.Id, reviewer, new TriageDecisionDto
        {
            Outcome = TriageOutcome.MarkDuplicate,
            Reason = "Same as the earlier report",
            DuplicateOfRequestId = original.Value!.Id
        });

        Assert.Equal(RequestStatus.Duplicate, result.Value!.Status);
        Assert.Null(result.Value.CreatedTaskId);

        var stored = await h.Db.Requests.SingleAsync(r => r.Id == copy.Value.Id);
        Assert.Equal(original.Value.Id, stored.RelatedRequestId);
    }

    [Fact]
    public async Task A_request_cannot_duplicate_itself_or_something_that_does_not_exist()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var created = await h.Requests.CreateAsync(requester, NewRequest());

        var itself = await h.Triage.DecideAsync(created.Value!.Id, reviewer, new TriageDecisionDto
        { Outcome = TriageOutcome.MarkDuplicate, Reason = "x", DuplicateOfRequestId = created.Value.Id });
        Assert.Equal("triage.duplicate_self", itself.Error!.Code);

        var missing = await h.Triage.DecideAsync(created.Value.Id, reviewer, new TriageDecisionDto
        { Outcome = TriageOutcome.MarkDuplicate, Reason = "x", DuplicateOfRequestId = 9999 });
        Assert.Equal("triage.duplicate_target_not_found", missing.Error!.Code);
    }

    [Fact]
    public async Task A_decided_request_cannot_be_decided_again()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var created = await h.Requests.CreateAsync(requester, NewRequest());
        await h.Triage.DecideAsync(created.Value!.Id, reviewer,
            new TriageDecisionDto { Outcome = TriageOutcome.Approve });

        var again = await h.Triage.DecideAsync(created.Value.Id, reviewer,
            new TriageDecisionDto { Outcome = TriageOutcome.Reject, Reason = "changed my mind" });

        Assert.Equal("request.already_decided", again.Error!.Code);
        // Critically: no second task.
        Assert.Equal(1, await h.Db.Tasks.CountAsync());
    }

    [Fact]
    public async Task The_review_queue_holds_only_items_a_reviewer_can_act_on()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var waiting = await h.Requests.CreateAsync(requester, NewRequest("Waiting for triage"));
        var withRequester = await h.Requests.CreateAsync(requester, NewRequest("Waiting on requester"));
        var approved = await h.Requests.CreateAsync(requester, NewRequest("Already approved"));

        await h.Triage.DecideAsync(withRequester.Value!.Id, reviewer, new TriageDecisionDto
        { Outcome = TriageOutcome.RequestClarification, Reason = "?" });
        await h.Triage.DecideAsync(approved.Value!.Id, reviewer,
            new TriageDecisionDto { Outcome = TriageOutcome.Approve });

        var queue = await h.Requests.ReviewQueueAsync(new PageQuery());

        // Clarification-required sits with the requester; approved is done. Neither belongs here.
        var only = Assert.Single(queue.Items);
        Assert.Equal(waiting.Value!.Id, only.Id);
    }

    [Fact]
    public async Task The_review_queue_puts_the_most_urgent_first()
    {
        var (h, requester, _) = await ReadyAsync();
        using var _d = h;

        await h.Requests.CreateAsync(requester, NewRequest("Low") with { RequestedUrgency = RequestedUrgency.Low });
        await h.Requests.CreateAsync(requester, NewRequest("Critical") with { RequestedUrgency = RequestedUrgency.Critical });
        await h.Requests.CreateAsync(requester, NewRequest("Normal") with { RequestedUrgency = RequestedUrgency.Normal });

        var queue = await h.Requests.ReviewQueueAsync(new PageQuery());

        Assert.Equal(new[] { "Critical", "Normal", "Low" }, queue.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Listing_can_be_scoped_to_one_requester()
    {
        var (h, requester, _) = await ReadyAsync();
        using var _d = h;
        var other = await h.CreateUserAsync("someone-else");

        await h.Requests.CreateAsync(requester, NewRequest("Mine"));
        await h.Requests.CreateAsync(other.Id, NewRequest("Theirs"));

        var mine = await h.Requests.ListAsync(new RequestQuery { RequestedByUserId = requester }, new PageQuery());

        var only = Assert.Single(mine.Items);
        Assert.Equal("Mine", only.Title);
    }

    [Fact]
    public async Task Approval_is_recorded_in_the_audit_log()
    {
        var (h, requester, reviewer) = await ReadyAsync();
        using var _d = h;

        var created = await h.Requests.CreateAsync(requester, NewRequest());
        await h.Triage.DecideAsync(created.Value!.Id, reviewer,
            new TriageDecisionDto { Outcome = TriageOutcome.Approve });

        Assert.True(await h.Db.AuditLogs.AnyAsync(
            a => a.Action == Common.Services.AuditActions.RequestApproved));
    }
}
