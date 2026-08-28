using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// The Quality page and the rest of the app have to agree about what is being checked.
///
/// They did not. The queue filtered on <c>CompletedReadyForQC</c> alone, so a task disappeared from
/// the Quality page the instant a checker claimed it — while every other screen went on saying
/// "Being checked", because that label covers <c>CompletedReadyForQC</c> <em>and</em>
/// <c>QCReview</c>. The work actually in hand was the part that vanished.
/// </summary>
public class QCQueueConsistencyTests
{
    private const string Criteria = "- The total is correct";

    private sealed record Fixture(TestHarness H, long TaskId, long WorkerId, long QCUserId);

    /// <summary>A task the worker has finished, sitting in the checker's queue.</summary>
    private static async Task<Fixture> HandedToQCAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("faisal");
        var reviewer = await h.CreateUserAsync("ahsan");
        var worker = await h.CreateUserAsync("hanzala");
        var qc = await h.CreateUserAsync("uzair");

        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Delivery order detail report total is wrong",
            Description = "The total row does not match the lines.",
            Type = RequestType.Bug
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.Normal,
            AcceptanceCriteria = Criteria
        });

        var task = await h.Db.Tasks.SingleAsync();
        await h.Assignment.AssignAsync(task.Id, reviewer.Id, new AssignTaskDto { AssigneeUserId = worker.Id });

        await h.StartShiftAsync(worker.Id);
        h.ActingAsAdmin(worker.Id);
        await h.WorkSessions.StartAsync(task.Id, worker.Id);
        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.WorkSessions.CompleteAsync(task.Id, worker.Id, "Fixed the total expression.");

        h.ActingAsAdmin(qc.Id);
        return new Fixture(h, task.Id, worker.Id, qc.Id);
    }

    private static PageQuery Page => new() { Page = 1, PageSize = 25 };

    [Fact]
    public async Task Work_handed_over_but_not_yet_claimed_is_in_the_queue()
    {
        var f = await HandedToQCAsync();
        using var _d = f.H;

        var queue = await f.H.QC.QueueAsync(Page);

        Assert.Contains(queue.Items, t => t.Id == f.TaskId);
    }

    /// <summary>
    /// The bug. Claiming a task moved it to <c>QCReview</c> and out of the only page a checker
    /// looks at, so the thing they were in the middle of was the one they could no longer find.
    /// </summary>
    [Fact]
    public async Task Work_already_being_checked_stays_in_the_queue()
    {
        var f = await HandedToQCAsync();
        using var _d = f.H;

        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);

        var task = await f.H.Db.Tasks.AsNoTracking().SingleAsync();
        Assert.Equal(WorkTaskStatus.QCReview, task.Status);

        var queue = await f.H.QC.QueueAsync(Page);
        Assert.Contains(queue.Items, t => t.Id == f.TaskId);
    }

    /// <summary>
    /// The queue and the word are now driven by the same table, which is what stops them drifting
    /// apart a second time.
    /// </summary>
    [Fact]
    public void The_queue_covers_exactly_what_being_checked_means()
    {
        var view = StatusViews.ForTasks(StatusAudience.Coordinator)
            .Single(v => v.Key == "checking");

        Assert.Equal(
            new[] { WorkTaskStatus.CompletedReadyForQC, WorkTaskStatus.QCReview },
            view.Statuses);
    }

    [Fact]
    public async Task Work_that_has_been_checked_leaves_the_queue()
    {
        var f = await HandedToQCAsync();
        using var _d = f.H;

        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Passed,
            Criteria = new[] { new AcceptanceCriterionVerdictDto { Index = 0, Met = true } }
        });

        var queue = await f.H.QC.QueueAsync(Page);

        Assert.DoesNotContain(queue.Items, t => t.Id == f.TaskId);
    }

    /// <summary>
    /// A failed check sends the work back to the person who did it, so it is theirs again rather
    /// than the checker's. It has to leave the queue too.
    /// </summary>
    [Fact]
    public async Task Work_sent_back_for_rework_leaves_the_queue()
    {
        var f = await HandedToQCAsync();
        using var _d = f.H;

        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Failed,
            Comments = "The total is still wrong on the second page.",
            Criteria = new[] { new AcceptanceCriterionVerdictDto { Index = 0, Met = false } }
        });

        var queue = await f.H.QC.QueueAsync(Page);

        Assert.DoesNotContain(queue.Items, t => t.Id == f.TaskId);
    }
}
