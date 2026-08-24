using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Several things asked for at once.
///
/// The tests that matter are the ones proving a batch is a wrapper and not a second workflow: each
/// item keeps its own number and its own triage decision, folding several into one task does not
/// bypass approval, and the batch cannot become work in its own right.
/// </summary>
public class RequestBatchTests
{
    private static readonly string[] WorkerRole = { "Worker" };

    private sealed record Fixture(TestHarness H, long RequesterId, long ReviewerId, long BatchId);

    private static async Task<Fixture> BatchOfThreeAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("rachel");
        var reviewer = await h.CreateUserAsync("victor");

        h.ActingAsAdmin(requester.Id);

        var batch = await h.Batches.CreateAsync(requester.Id, new CreateRequestBatchDto
        {
            Title = "Month-end problems",
            Note = "All found during the July close.",
            ClientName = "Falcon Traders",
            Items = new[]
            {
                new BatchItemDto { Title = "Ledger export is slow", Description = "Takes an hour.", Type = RequestType.Investigation },
                new BatchItemDto { Title = "VAT line double-counts", Description = "On credit notes.", Type = RequestType.Bug, RequestedUrgency = RequestedUrgency.Critical },
                new BatchItemDto { Title = "Add a summary column", Description = "By client.", Type = RequestType.ChangeRequest },
            },
        });

        Assert.True(batch.IsSuccess);
        return new Fixture(h, requester.Id, reviewer.Id, batch.Value!.Id);
    }

    [Fact]
    public async Task CreateAsync_gives_every_item_its_own_request_number()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;

        Assert.Equal(3, detail.Items.Count);
        Assert.Equal(3, detail.Items.Select(i => i.RequestNumber).Distinct().Count());
        Assert.All(detail.Items, i => Assert.StartsWith("REQ-", i.RequestNumber));

        // The batch has its own counter — a printed number must not share a sequence.
        Assert.StartsWith("BAT-", detail.BatchNumber);

        // Ordered as they were typed.
        Assert.Equal(new[] { 1, 2, 3 }, detail.Items.Select(i => i.Ordinal).ToArray());
    }

    [Fact]
    public async Task CreateAsync_copies_the_client_onto_each_item()
    {
        var f = await BatchOfThreeAsync();

        var items = await f.H.Db.Requests.Where(r => r.BatchId == f.BatchId).ToListAsync();

        // Copied, not read through the batch: an item corrected at triage must not drag its
        // siblings with it.
        Assert.All(items, i => Assert.NotNull(i.ClientId));
        Assert.Single(items.Select(i => i.ClientId).Distinct());
    }

    [Fact]
    public async Task CreateAsync_refuses_a_batch_of_nothing()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        var requester = await h.CreateUserAsync("rachel");
        h.ActingAsAdmin(requester.Id);

        var result = await h.Batches.CreateAsync(requester.Id, new CreateRequestBatchDto
        {
            Title = "Nothing here",
            Items = new[] { new BatchItemDto { Title = "  ", Description = "  " } },
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("batch.no_items", result.Error!.Code);
    }

    [Fact]
    public async Task CreateAsync_refuses_a_half_filled_item()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        var requester = await h.CreateUserAsync("rachel");
        h.ActingAsAdmin(requester.Id);

        var result = await h.Batches.CreateAsync(requester.Id, new CreateRequestBatchDto
        {
            Title = "Half typed",
            Items = new[]
            {
                new BatchItemDto { Title = "Fine", Description = "Complete." },
                new BatchItemDto { Title = "Started but abandoned", Description = "   " },
            },
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("batch.item_incomplete", result.Error!.Code);
    }

    [Fact]
    public async Task An_item_is_triaged_like_any_other_request()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;
        var third = detail.Items[2];

        f.H.ActingAsAdmin(f.ReviewerId);

        // Rejecting one item must leave its siblings entirely alone — that is the whole reason a
        // batch carries no status of its own.
        var rejected = await f.H.Triage.DecideAsync(third.Id, f.ReviewerId, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Reject,
            Reason = "Already covered by another piece of work.",
        });

        Assert.True(rejected.IsSuccess);

        var after = (await f.H.Batches.GetAsync(f.BatchId)).Value!;
        Assert.Equal(RequestStatus.Rejected, after.Items[2].Status);
        Assert.Equal(RequestStatus.Submitted, after.Items[0].Status);
        Assert.Equal(RequestStatus.Submitted, after.Items[1].Status);
    }

    [Fact]
    public async Task ApproveTogetherAsync_folds_several_items_into_one_task()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;
        var chosen = new[] { detail.Items[0].Id, detail.Items[1].Id };

        f.H.ActingAsAdmin(f.ReviewerId);

        var result = await f.H.Batches.ApproveTogetherAsync(f.BatchId, f.ReviewerId,
            new ApproveTogetherDto { RequestIds = chosen, EstimatedEffortHours = 8 });

        Assert.True(result.IsSuccess);

        // One task, not two.
        var tasks = await f.H.Db.Tasks.ToListAsync();
        Assert.Single(tasks);

        // Both items point at it, and both are approved in their own right.
        var items = await f.H.Db.Requests.Where(r => chosen.Contains(r.Id)).ToListAsync();
        Assert.All(items, i => Assert.Equal(RequestStatus.Approved, i.Status));
        Assert.All(items, i => Assert.Equal(tasks[0].Id, i.GeneratedTaskId));

        // The task was raised from the first item — that is what WorkTask.RequestId means.
        Assert.Equal(detail.Items[0].Id, tasks[0].RequestId);

        // The untouched third item is still waiting.
        var third = await f.H.Db.Requests.FirstAsync(r => r.Id == detail.Items[2].Id);
        Assert.Equal(RequestStatus.Submitted, third.Status);
        Assert.Null(third.GeneratedTaskId);
    }

    [Fact]
    public async Task A_folded_task_carries_every_item_in_its_description()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;

        f.H.ActingAsAdmin(f.ReviewerId);
        await f.H.Batches.ApproveTogetherAsync(f.BatchId, f.ReviewerId, new ApproveTogetherDto
        {
            RequestIds = new[] { detail.Items[0].Id, detail.Items[1].Id },
        });

        var task = await f.H.Db.Tasks.SingleAsync();

        // A worker handed two folded requests has to see both, or "done" gets declared when only
        // the first one is.
        Assert.Contains(detail.Items[0].RequestNumber, task.Description);
        Assert.Contains(detail.Items[1].RequestNumber, task.Description);
        Assert.Contains("Takes an hour.", task.Description);
        Assert.Contains("On credit notes.", task.Description);
    }

    [Fact]
    public async Task Folding_takes_the_highest_urgency_across_the_items()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;

        f.H.ActingAsAdmin(f.ReviewerId);

        // Item 1 is Normal, item 2 is Critical. Taking the first, or averaging, would let a
        // critical item be quietly downgraded by being submitted next to a trivial one.
        await f.H.Batches.ApproveTogetherAsync(f.BatchId, f.ReviewerId, new ApproveTogetherDto
        {
            RequestIds = new[] { detail.Items[0].Id, detail.Items[1].Id },
        });

        var task = await f.H.Db.Tasks.SingleAsync();
        Assert.Equal(Priority.Critical, task.Priority);
    }

    [Fact]
    public async Task The_task_traces_back_to_every_request_that_produced_it()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;

        f.H.ActingAsAdmin(f.ReviewerId);
        var approved = await f.H.Batches.ApproveTogetherAsync(f.BatchId, f.ReviewerId,
            new ApproveTogetherDto { RequestIds = new[] { detail.Items[0].Id, detail.Items[1].Id } });

        var task = (await f.H.TaskQueries.GetAsync(approved.Value!.CreatedTaskId!.Value)).Value!;

        // batch → item → task, from the task's end.
        Assert.NotNull(task.Request);
        Assert.Equal(detail.BatchNumber, task.Request!.BatchNumber);
        Assert.NotNull(task.Request.FoldedWith);
        Assert.Single(task.Request.FoldedWith!);
        Assert.Equal(detail.Items[1].RequestNumber, task.Request.FoldedWith![0].RequestNumber);

        // ...and from the batch's end.
        var after = (await f.H.Batches.GetAsync(f.BatchId)).Value!;
        Assert.Equal(approved.Value.CreatedTaskNumber, after.Items[0].GeneratedTaskNumber);
        Assert.Equal(approved.Value.CreatedTaskNumber, after.Items[1].GeneratedTaskNumber);
        Assert.Contains(detail.Items[1].RequestNumber, after.Items[0].SharedTaskWith);
        Assert.Contains(detail.Items[0].RequestNumber, after.Items[1].SharedTaskWith);
        Assert.Empty(after.Items[2].SharedTaskWith);
    }

    [Fact]
    public async Task ApproveTogetherAsync_refuses_an_item_already_decided()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;

        f.H.ActingAsAdmin(f.ReviewerId);
        await f.H.Triage.DecideAsync(detail.Items[0].Id, f.ReviewerId, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Reject,
            Reason = "Not proceeding.",
        });

        var result = await f.H.Batches.ApproveTogetherAsync(f.BatchId, f.ReviewerId,
            new ApproveTogetherDto { RequestIds = new[] { detail.Items[0].Id, detail.Items[1].Id } });

        Assert.False(result.IsSuccess);
        Assert.Equal("batch.item_already_decided", result.Error!.Code);

        // And nothing was created on the way to refusing.
        Assert.Empty(await f.H.Db.Tasks.ToListAsync());
    }

    [Fact]
    public async Task ApproveTogetherAsync_refuses_an_item_from_another_batch()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;

        f.H.ActingAsAdmin(f.RequesterId);
        var other = await f.H.Batches.CreateAsync(f.RequesterId, new CreateRequestBatchDto
        {
            Title = "A different submission",
            Items = new[] { new BatchItemDto { Title = "Unrelated", Description = "Elsewhere." } },
        });

        var stranger = (await f.H.Batches.GetAsync(other.Value!.Id)).Value!.Items[0];

        f.H.ActingAsAdmin(f.ReviewerId);
        var result = await f.H.Batches.ApproveTogetherAsync(f.BatchId, f.ReviewerId,
            new ApproveTogetherDto { RequestIds = new[] { detail.Items[0].Id, stranger.Id } });

        Assert.False(result.IsSuccess);
        Assert.Equal("batch.item_not_in_batch", result.Error!.Code);
    }

    [Fact]
    public async Task ApproveTogetherAsync_refuses_while_a_question_is_unanswered()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;

        f.H.ActingAsAdmin(f.ReviewerId);
        await f.H.Triage.DecideAsync(detail.Items[1].Id, f.ReviewerId, new TriageDecisionDto
        {
            Outcome = TriageOutcome.RequestClarification,
            Reason = "Which credit notes?",
        });

        var result = await f.H.Batches.ApproveTogetherAsync(f.BatchId, f.ReviewerId,
            new ApproveTogetherDto { RequestIds = new[] { detail.Items[0].Id, detail.Items[1].Id } });

        Assert.False(result.IsSuccess);
        Assert.Equal("request.clarification_pending", result.Error!.Code);
    }

    [Fact]
    public async Task Every_folded_item_gets_its_own_audit_row()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;

        f.H.ActingAsAdmin(f.ReviewerId);
        await f.H.Batches.ApproveTogetherAsync(f.BatchId, f.ReviewerId, new ApproveTogetherDto
        {
            RequestIds = new[] { detail.Items[0].Id, detail.Items[1].Id },
        });

        // An administrator asking "who approved REQ-000002" must find an answer against that
        // request, not against a batch operation they then have to unpick.
        var rows = await f.H.Db.AuditLogs
            .Where(a => a.Action == AuditActions.RequestApproved)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, a => a.EntityId == detail.Items[0].Id);
        Assert.Contains(rows, a => a.EntityId == detail.Items[1].Id);
    }

    [Fact]
    public async Task The_review_queue_counts_what_still_needs_a_decision()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;

        f.H.ActingAsAdmin(f.ReviewerId);
        await f.H.Triage.DecideAsync(detail.Items[2].Id, f.ReviewerId, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Reject,
            Reason = "Not proceeding.",
        });

        var queue = await f.H.Batches.ReviewQueueAsync(new PageQuery());
        var row = Assert.Single<RequestBatchSummaryDto>(queue.Items);

        Assert.Equal(3, row.ItemCount);
        Assert.Equal(2, row.AwaitingDecisionCount);
        Assert.Equal(1, row.DeclinedCount);
        Assert.Equal(0, row.ApprovedCount);

        // Once every item is decided the batch leaves the queue — a reviewer's list must not fill
        // up with work they have already done.
        await f.H.Batches.ApproveTogetherAsync(f.BatchId, f.ReviewerId, new ApproveTogetherDto
        {
            RequestIds = new[] { detail.Items[0].Id, detail.Items[1].Id },
        });

        Assert.Empty((await f.H.Batches.ReviewQueueAsync(new PageQuery())).Items);
    }

    [Fact]
    public async Task A_batch_item_still_reports_its_own_progress_to_the_requester()
    {
        var f = await BatchOfThreeAsync();
        var detail = (await f.H.Batches.GetAsync(f.BatchId)).Value!;

        f.H.ActingAsAdmin(f.ReviewerId);
        await f.H.Batches.ApproveTogetherAsync(f.BatchId, f.ReviewerId, new ApproveTogetherDto
        {
            RequestIds = new[] { detail.Items[0].Id, detail.Items[1].Id },
        });

        // The point of folding onto GeneratedTaskId rather than a join table: every existing read
        // path keeps working, so the *second* item reports the shared task's progress too.
        var second = (await f.H.Requests.GetAsync(detail.Items[1].Id, StatusAudience.Requester)).Value!;

        Assert.NotNull(second.GeneratedTaskId);
        Assert.NotNull(second.Progress);
    }
}
