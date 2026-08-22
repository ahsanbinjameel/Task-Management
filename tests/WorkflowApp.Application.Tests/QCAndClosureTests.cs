using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Phase 7: quality control and closure. The through-line is that a task cannot reach Closed
/// without leaving evidence behind it — a numbered QC attempt, an evaluated set of acceptance
/// criteria, and a written resolution.
/// </summary>
public class QCAndClosureTests
{
    private const string Criteria =
        "- POD photos render on the invoice PDF\n- Existing invoices are unaffected\n- Regression test added";

    private sealed record Fixture(TestHarness H, long TaskId, long WorkerId, long QCUserId, long CoordinatorId);

    /// <summary>A task the worker has completed, sitting in CompletedReadyForQC with criteria set.</summary>
    private static async Task<Fixture> ReadyForQCAsync(string? acceptanceCriteria = Criteria)
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("rachel");
        var reviewer = await h.CreateUserAsync("victor");
        var coordinator = await h.CreateUserAsync("amara");
        var worker = await h.CreateUserAsync("wu");
        var qc = await h.CreateUserAsync("quentin");

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

        h.ActingAsAdmin(coordinator.Id);
        await h.Assignment.AssignAsync(task.Id, coordinator.Id,
            new AssignTaskDto { AssigneeUserId = worker.Id });

        await h.Assignment.UpdateDetailsAsync(task.Id, coordinator.Id,
            new UpdateTaskDetailsDto { AcceptanceCriteria = acceptanceCriteria });

        await h.StartShiftAsync(worker.Id);
        h.ActingAsAdmin(worker.Id);
        await h.WorkSessions.StartAsync(task.Id, worker.Id);
        h.Clock.Advance(TimeSpan.FromHours(2));
        await h.WorkSessions.CompleteAsync(task.Id, worker.Id, "Corrected the PDF template.");

        h.ActingAsAdmin(qc.Id);
        return new Fixture(h, task.Id, worker.Id, qc.Id, coordinator.Id);
    }

    private static async Task<Fixture> UnderReviewAsync(string? acceptanceCriteria = Criteria)
    {
        var f = await ReadyForQCAsync(acceptanceCriteria);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        return f;
    }

    private static IReadOnlyList<AcceptanceCriterionVerdictDto> AllMet(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new AcceptanceCriterionVerdictDto { Index = i, Met = true })
            .ToList();

    // --- acceptance criteria parsing -------------------------------------------------------

    [Theory]
    [InlineData("- one\n- two", 2)]
    [InlineData("1. one\r\n2) two\r\n\r\n3. three", 3)]
    [InlineData("- [ ] one\n- [x] two", 2)]
    [InlineData("   \n  \n", 0)]
    [InlineData(null, 0)]
    public void Parse_strips_list_markers_and_blank_lines(string? text, int expected)
        => Assert.Equal(expected, AcceptanceCriteria.Parse(text).Count);

    [Fact]
    public void Parse_keeps_the_criterion_text_without_its_marker()
        => Assert.Equal("POD photos render", AcceptanceCriteria.Parse("- [ ] POD photos render").Single());

    // --- starting a review -----------------------------------------------------------------

    [Fact]
    public async Task Starting_qc_claims_the_task_and_moves_it_under_review()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        var result = await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.QCReview, result.Value!.Status);
        Assert.Equal(f.QCUserId, result.Value.QCUserId);
    }

    [Fact]
    public async Task Starting_qc_is_refused_to_the_assignee()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        var result = await f.H.QC.StartReviewAsync(f.TaskId, f.WorkerId);

        Assert.Equal("qc.reviewer_is_assignee", result.Error!.Code);
    }

    [Fact]
    public async Task Starting_qc_is_refused_to_someone_else_once_a_reviewer_owns_it()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;
        var other = await f.H.CreateUserAsync("morgan");

        var result = await f.H.QC.StartReviewAsync(f.TaskId, other.Id);

        Assert.Equal("qc.not_qc_owner", result.Error!.Code);
    }

    [Fact]
    public async Task Starting_qc_on_work_that_is_not_finished_is_a_conflict()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Failed,
            Comments = "Photos are stretched."
        });

        var result = await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);

        Assert.Equal("qc.not_ready", result.Error!.Code);
    }

    // --- verdicts --------------------------------------------------------------------------

    [Fact]
    public async Task Passing_qc_requires_every_criterion_to_be_evaluated()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;

        var result = await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Passed,
            Criteria = AllMet(2)   // three are declared on the task
        });

        Assert.Equal("qc.criteria_incomplete", result.Error!.Code);
    }

    [Fact]
    public async Task Passing_qc_is_refused_while_a_criterion_is_unmet()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;

        var verdicts = AllMet(3).ToList();
        verdicts[1] = new AcceptanceCriterionVerdictDto { Index = 1, Met = false, Note = "Old invoices broke." };

        var result = await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Passed,
            Criteria = verdicts
        });

        Assert.Equal("qc.criteria_unmet", result.Error!.Code);
        Assert.Equal(WorkTaskStatus.QCReview, (await f.H.Db.Tasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task Passing_qc_records_the_attempt_and_the_evaluated_criteria()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;

        var result = await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Passed,
            Criteria = AllMet(3),
            Environment = "Staging",
            BuildVersion = "2026.8.4"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.QCPassed, result.Value!.Status);

        var review = await f.H.Db.QCReviews.SingleAsync();
        Assert.Equal(1, review.AttemptNumber);
        Assert.Equal(QCResult.Passed, review.Result);
        Assert.Equal("Staging", review.Environment);

        var stored = AcceptanceCriteria.Deserialize(review.AcceptanceCriteriaResults);
        Assert.Equal(3, stored.Count);
        Assert.All(stored, c => Assert.True(c.Met));
    }

    [Fact]
    public async Task Failing_qc_without_comments_is_rejected()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;

        var result = await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId,
            new SubmitQCReviewDto { Result = QCResult.Failed });

        Assert.Equal("qc.comments_required", result.Error!.Code);
    }

    [Fact]
    public async Task Failing_qc_sends_the_task_to_rework_never_to_closed()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;

        var result = await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Failed,
            Comments = "Photos are stretched on A4."
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.QCFailedRework, result.Value!.Status);
    }

    [Fact]
    public async Task A_query_from_qc_records_an_attempt_but_leaves_the_task_under_review()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;

        var result = await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.ClarificationRequired,
            Comments = "Which template version was this built against?"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.QCReview, result.Value!.Status);
        Assert.Equal(QCResult.ClarificationRequired, (await f.H.Db.QCReviews.SingleAsync()).Result);
    }

    [Fact]
    public async Task Every_qc_attempt_is_kept_and_numbered_in_sequence()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;

        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Failed,
            Comments = "Photos are stretched."
        });

        // Rework, complete again, second review.
        f.H.ActingAsAdmin(f.WorkerId);
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromMinutes(45));
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Aspect ratio fixed.");

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Passed,
            Criteria = AllMet(3)
        });

        var history = await f.H.QC.HistoryAsync(f.TaskId);
        Assert.Equal(2, history.Count);
        Assert.Equal(new[] { 1, 2 }, history.Select(r => r.AttemptNumber));
        Assert.Equal(QCResult.Failed, history[0].Result);   // the failure is not erased by the pass
        Assert.Equal(QCResult.Passed, history[1].Result);
    }

    [Fact]
    public async Task A_task_with_no_criteria_can_pass_without_verdicts()
    {
        var f = await UnderReviewAsync(acceptanceCriteria: null);
        using var _d = f.H;

        var result = await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId,
            new SubmitQCReviewDto { Result = QCResult.Passed });

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.QCPassed, result.Value!.Status);
    }

    [Fact]
    public async Task Qc_verdicts_are_reported_against_the_current_criteria()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Passed,
            Criteria = AllMet(3)
        });

        var criteria = await f.H.QC.CriteriaAsync(f.TaskId);

        Assert.True(criteria.IsSuccess);
        Assert.Equal(3, criteria.Value!.Criteria.Count);
        Assert.Equal(1, criteria.Value.EvaluatedInAttempt);
        Assert.All(criteria.Value.Criteria, c => Assert.True(c.Met));
    }

    [Fact]
    public async Task Rewriting_the_criteria_after_qc_drops_the_stale_verdicts()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Passed,
            Criteria = AllMet(3)
        });

        f.H.ActingAsAdmin(f.CoordinatorId);
        await f.H.Assignment.UpdateDetailsAsync(f.TaskId, f.CoordinatorId,
            new UpdateTaskDetailsDto { AcceptanceCriteria = Criteria + "\n- Documentation updated" });

        var criteria = await f.H.QC.CriteriaAsync(f.TaskId);

        Assert.Equal(4, criteria.Value!.Criteria.Count);
        Assert.Null(criteria.Value.Criteria[3].Met);   // never evaluated, so not silently inherited
    }

    // --- the generic transition endpoint cannot short-circuit QC or closure ------------------

    [Theory]
    [InlineData(WorkTaskStatus.QCPassed)]
    [InlineData(WorkTaskStatus.QCFailedRework)]
    public async Task Qc_states_cannot_be_set_through_the_generic_transition(WorkTaskStatus to)
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;

        var result = await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.QCUserId,
            new TransitionTaskDto { To = to, Reason = "looks fine" });

        Assert.Equal("workflow.dedicated_endpoint_required", result.Error!.Code);
        Assert.Empty(await f.H.Db.QCReviews.ToListAsync());
    }

    [Fact]
    public async Task An_override_can_still_force_a_guarded_state()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;

        var result = await f.H.TaskWorkflow.TransitionAsync(f.TaskId, f.QCUserId, new TransitionTaskDto
        {
            To = WorkTaskStatus.QCPassed,
            IsOverride = true,
            Reason = "QC tooling outage; signed off out of band by the head of delivery."
        });

        Assert.True(result.IsSuccess);
        Assert.True((await f.H.Db.StatusHistories.SingleAsync(h => h.ToStatus == WorkTaskStatus.QCPassed))
            .WasOverride);
    }

    // --- closure -----------------------------------------------------------------------------

    private static async Task<Fixture> QCPassedAsync()
    {
        var f = await UnderReviewAsync();
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Passed,
            Criteria = AllMet(3)
        });
        return f;
    }

    [Fact]
    public async Task Closure_is_refused_before_qc_has_passed()
    {
        var f = await UnderReviewAsync();
        using var _d = f.H;

        var result = await f.H.Closure.CloseAsync(f.TaskId, f.QCUserId, new CloseTaskDto());

        Assert.Equal("closure.not_ready_to_close", result.Error!.Code);
    }

    [Fact]
    public async Task Closure_requires_a_resolution()
    {
        var f = await QCPassedAsync();
        using var _d = f.H;

        var task = await f.H.Db.Tasks.SingleAsync();
        task.Resolution = null;
        await f.H.Db.SaveChangesAsync();

        var result = await f.H.Closure.CloseAsync(f.TaskId, f.QCUserId, new CloseTaskDto());

        Assert.Equal("closure.requirements_unmet", result.Error!.Code);

        // Supplying one at closure time satisfies the requirement.
        var second = await f.H.Closure.CloseAsync(f.TaskId, f.QCUserId,
            new CloseTaskDto { Resolution = "Template corrected and released." });

        Assert.True(second.IsSuccess);
    }

    [Fact]
    public async Task Closure_is_refused_while_a_subtask_is_still_open()
    {
        var f = await QCPassedAsync();
        using var _d = f.H;

        var parent = await f.H.Db.Tasks.SingleAsync();
        f.H.Db.Tasks.Add(new Domain.Entities.Tasks.WorkTask
        {
            TaskNumber = "TSK-000999",
            Title = "Backfill historic invoices",
            Description = "Regenerate PDFs for the last quarter.",
            ParentTaskId = parent.Id,
            Status = WorkTaskStatus.InProgress
        });
        await f.H.Db.SaveChangesAsync();

        var check = await f.H.Closure.EvaluateAsync(f.TaskId);

        Assert.False(check.Value!.IsReady);
        Assert.False(check.Value.Requirements.Single(r => r.Code == "closure.subtasks_closed").IsMet);
        Assert.Equal("closure.requirements_unmet",
            (await f.H.Closure.CloseAsync(f.TaskId, f.QCUserId, new CloseTaskDto())).Error!.Code);
    }

    [Fact]
    public async Task Closure_is_refused_when_the_criteria_were_widened_after_qc_passed()
    {
        var f = await QCPassedAsync();
        using var _d = f.H;

        f.H.ActingAsAdmin(f.CoordinatorId);
        await f.H.Assignment.UpdateDetailsAsync(f.TaskId, f.CoordinatorId,
            new UpdateTaskDetailsDto { AcceptanceCriteria = Criteria + "\n- Runbook updated" });

        var check = await f.H.Closure.EvaluateAsync(f.TaskId);

        Assert.False(check.Value!.Requirements.Single(r => r.Code == "closure.criteria_met").IsMet);
    }

    [Fact]
    public async Task Closing_a_passed_task_walks_through_ready_for_closure_and_records_both_steps()
    {
        var f = await QCPassedAsync();
        using var _d = f.H;

        var check = await f.H.Closure.EvaluateAsync(f.TaskId);
        Assert.True(check.Value!.IsReady);

        var result = await f.H.Closure.CloseAsync(f.TaskId, f.QCUserId,
            new CloseTaskDto { Reason = "Verified in staging." });

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.Closed, result.Value!.Status);

        var trail = await f.H.Db.StatusHistories
            .Where(h => h.TaskId == f.TaskId)
            .OrderBy(h => h.Id)
            .Select(h => h.ToStatus)
            .ToListAsync();

        Assert.Contains(WorkTaskStatus.ReadyForClosure, trail);
        Assert.Equal(WorkTaskStatus.Closed, trail[^1]);
    }

    [Fact]
    public async Task Closing_an_already_closed_task_is_a_no_op()
    {
        var f = await QCPassedAsync();
        using var _d = f.H;
        await f.H.Closure.CloseAsync(f.TaskId, f.QCUserId, new CloseTaskDto());

        var before = await f.H.Db.StatusHistories.CountAsync();
        var again = await f.H.Closure.CloseAsync(f.TaskId, f.QCUserId, new CloseTaskDto());

        Assert.True(again.IsSuccess);
        Assert.Equal(before, await f.H.Db.StatusHistories.CountAsync());
    }

    [Fact]
    public async Task Closure_is_audited()
    {
        var f = await QCPassedAsync();
        using var _d = f.H;

        await f.H.Closure.CloseAsync(f.TaskId, f.QCUserId, new CloseTaskDto());

        Assert.True(await f.H.Db.AuditLogs.AnyAsync(a => a.Action == "Task.Closed"));
        Assert.True(await f.H.Db.AuditLogs.AnyAsync(a => a.Action == "Task.QCPassed"));
    }
}
