using System.Text;
using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Files carry a <see cref="AttachmentKind"/> saying what they are <i>for</i>, as opposed to what
/// they hang off. The through-line: the screenshot describing a problem and the screenshot proving
/// it was fixed must never end up in the same undifferentiated list, and neither may be claimed by
/// somebody who is not entitled to make that claim.
/// </summary>
public class AttachmentProofTests
{
    private sealed record Fixture(
        TestHarness H, long TaskId, long RequestId, long WorkerId, long QCUserId, long CoordinatorId);

    /// <summary>A task the worker has finished, waiting for a quality check.</summary>
    private static async Task<Fixture> ReadyForQCAsync()
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
            EstimatedEffortHours = 4m
        });

        var task = await h.Db.Tasks.SingleAsync();

        h.ActingAsAdmin(coordinator.Id);
        await h.Assignment.AssignAsync(task.Id, coordinator.Id,
            new AssignTaskDto { AssigneeUserId = worker.Id });

        await h.Assignment.UpdateDetailsAsync(task.Id, coordinator.Id,
            new UpdateTaskDetailsDto { AcceptanceCriteria = "- POD photos render on the invoice PDF" });

        await h.StartShiftAsync(worker.Id);
        h.ActingAsAdmin(worker.Id);
        await h.WorkSessions.StartAsync(task.Id, worker.Id);
        h.Clock.Advance(TimeSpan.FromHours(2));
        await h.WorkSessions.CompleteAsync(task.Id, worker.Id, "Corrected the PDF template.");

        return new Fixture(h, task.Id, request.Value.Id, worker.Id, qc.Id, coordinator.Id);
    }

    private static Stream File(string text = "PNG") => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static Task<Result<AttachmentDto>> UploadToTaskAsync(
        Fixture f, long uploaderId, string name, AttachmentKind kind) =>
        f.H.Attachments.UploadAsync(
            requestId: null, taskId: f.TaskId, uploaderId: uploaderId,
            File(), name, "image/png", default, batchId: null, kind: kind);

    // --- who may claim to have proved what -------------------------------------------------

    [Fact]
    public async Task Completion_proof_is_accepted_from_the_person_responsible_for_the_work()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        f.H.ActingAsAdmin(f.WorkerId);
        var result = await UploadToTaskAsync(f, f.WorkerId, "fixed.png", AttachmentKind.CompletionProof);

        Assert.True(result.IsSuccess);

        var stored = await f.H.Db.Attachments.SingleAsync(a => a.Id == result.Value!.Id);
        Assert.Equal(AttachmentKind.CompletionProof, stored.Kind);
        Assert.Null(stored.QCReviewId);
    }

    [Fact]
    public async Task Completion_proof_is_refused_to_anyone_but_the_assignee()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        // A coordinator holds every permission there is and still cannot supply the proof: whether
        // the work was done is the responsible person's claim to make.
        f.H.ActingAsAdmin(f.CoordinatorId);
        var result = await UploadToTaskAsync(f, f.CoordinatorId, "fixed.png", AttachmentKind.CompletionProof);

        Assert.Equal("attachment.not_assignee", result.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
    }

    [Fact]
    public async Task Quality_check_evidence_is_refused_without_the_checking_permission()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        f.H.ActingAs(f.WorkerId, Permissions.TaskWork);
        var result = await UploadToTaskAsync(f, f.WorkerId, "shot.png", AttachmentKind.QCEvidence);

        Assert.Equal("attachment.not_checker", result.Error!.Code);
    }

    [Fact]
    public async Task Proof_cannot_hang_off_a_request()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        f.H.ActingAsAdmin(f.WorkerId);
        var result = await f.H.Attachments.UploadAsync(
            requestId: f.RequestId, taskId: null, uploaderId: f.WorkerId,
            File(), "fixed.png", "image/png", default, batchId: null,
            kind: AttachmentKind.CompletionProof);

        // Proof of work that has not been created yet is a claim about nothing.
        Assert.Equal("attachment.kind_needs_task", result.Error!.Code);
    }

    [Fact]
    public async Task An_attachment_still_needs_exactly_one_owner()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        f.H.ActingAsAdmin(f.WorkerId);

        var none = await f.H.Attachments.UploadAsync(
            requestId: null, taskId: null, uploaderId: f.WorkerId,
            File(), "note.png", "image/png");

        var both = await f.H.Attachments.UploadAsync(
            requestId: f.RequestId, taskId: f.TaskId, uploaderId: f.WorkerId,
            File(), "note.png", "image/png");

        Assert.Equal("attachment.owner_required", none.Error!.Code);
        Assert.Equal("attachment.owner_required", both.Error!.Code);
    }

    // --- evidence and the attempt it justified ----------------------------------------------

    [Fact]
    public async Task Evidence_staged_before_the_verdict_is_tied_to_the_attempt_that_followed()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);

        var staged = await UploadToTaskAsync(f, f.QCUserId, "attempt-1.png", AttachmentKind.QCEvidence);
        Assert.True(staged.IsSuccess);

        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Failed,
            Comments = "The photo is rotated."
        });

        var attempt = await f.H.Db.QCReviews.SingleAsync();
        var evidence = await f.H.Db.Attachments.SingleAsync(a => a.Id == staged.Value!.Id);

        Assert.Equal(attempt.Id, evidence.QCReviewId);
    }

    [Fact]
    public async Task Each_attempt_keeps_its_own_evidence()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        var first = await UploadToTaskAsync(f, f.QCUserId, "attempt-1.png", AttachmentKind.QCEvidence);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Failed,
            Comments = "The photo is rotated."
        });

        // Round two: the work comes back, is finished again, and is checked again.
        f.H.ActingAsAdmin(f.WorkerId);
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromHours(1));
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Rotation corrected.");

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        var second = await UploadToTaskAsync(f, f.QCUserId, "attempt-2.png", AttachmentKind.QCEvidence);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Passed,
            Criteria = new List<AcceptanceCriterionVerdictDto>
            {
                new() { Index = 0, Met = true }
            }
        });

        var attempts = await f.H.Db.QCReviews.OrderBy(q => q.AttemptNumber).ToListAsync();
        var files = await f.H.Db.Attachments
            .Where(a => a.Kind == AttachmentKind.QCEvidence)
            .ToDictionaryAsync(a => a.OriginalFileName, a => a.QCReviewId);

        // The pictures that justified the failure stay with the failure once a later attempt passes.
        Assert.Equal(attempts[0].Id, files["attempt-1.png"]);
        Assert.Equal(attempts[1].Id, files["attempt-2.png"]);
        Assert.NotEqual(first.Value!.Id, second.Value!.Id);
    }

    [Fact]
    public async Task One_checkers_evidence_is_not_swept_onto_another_checkers_verdict()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        var otherChecker = await f.H.CreateUserAsync("priya");

        // Two people with the checking permission are looking at the same task; only one of them
        // records a verdict.
        f.H.ActingAsAdmin(otherChecker.Id);
        var theirs = await UploadToTaskAsync(f, otherChecker.Id, "theirs.png", AttachmentKind.QCEvidence);

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        var mine = await UploadToTaskAsync(f, f.QCUserId, "mine.png", AttachmentKind.QCEvidence);

        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Failed,
            Comments = "Still rotated."
        });

        var attempt = await f.H.Db.QCReviews.SingleAsync();

        Assert.Equal(attempt.Id, (await f.H.Db.Attachments.SingleAsync(a => a.Id == mine.Value!.Id)).QCReviewId);
        Assert.Null((await f.H.Db.Attachments.SingleAsync(a => a.Id == theirs.Value!.Id)).QCReviewId);
    }

    [Fact]
    public async Task A_refused_verdict_leaves_the_evidence_staged_for_the_retry()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        var staged = await UploadToTaskAsync(f, f.QCUserId, "shot.png", AttachmentKind.QCEvidence);

        // A pass with an unmet criterion is refused, so no attempt is written at all.
        var refused = await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Passed,
            Criteria = new List<AcceptanceCriterionVerdictDto>
            {
                new() { Index = 0, Met = false, Note = "Still rotated." }
            }
        });

        Assert.False(refused.IsSuccess);
        Assert.Empty(await f.H.Db.QCReviews.ToListAsync());
        Assert.Null((await f.H.Db.Attachments.SingleAsync(a => a.Id == staged.Value!.Id)).QCReviewId);

        // The retry picks the same files up.
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Failed,
            Comments = "Still rotated."
        });

        var attempt = await f.H.Db.QCReviews.SingleAsync();
        Assert.Equal(attempt.Id, (await f.H.Db.Attachments.SingleAsync(a => a.Id == staged.Value!.Id)).QCReviewId);
    }

    // --- what the task detail hands back -----------------------------------------------------

    [Fact]
    public async Task The_detail_separates_proof_from_context_and_leaves_evidence_with_its_attempt()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        f.H.ActingAsAdmin(f.WorkerId);
        await UploadToTaskAsync(f, f.WorkerId, "fixed.png", AttachmentKind.CompletionProof);
        await UploadToTaskAsync(f, f.WorkerId, "notes.txt", AttachmentKind.General);

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        await UploadToTaskAsync(f, f.QCUserId, "checked.png", AttachmentKind.QCEvidence);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto
        {
            Result = QCResult.Failed,
            Comments = "The photo is rotated."
        });

        var detail = await f.H.TaskQueries.GetAsync(f.TaskId);
        var t = detail.Value!;

        Assert.Equal(new[] { "fixed.png" }, t.CompletionProof!.Select(a => a.FileName));

        // QC evidence is left out of both loose lists: it belongs to a numbered attempt, and on its
        // own it loses the one thing that makes it mean anything.
        Assert.Equal(new[] { "notes.txt" }, t.Attachments!.Select(a => a.FileName));
        Assert.Equal(new[] { "checked.png" }, t.QCReviews.Single().Attachments!.Select(a => a.FileName));
    }

    [Fact]
    public async Task Request_screenshots_stay_on_the_request_and_out_of_the_proof()
    {
        var f = await ReadyForQCAsync();
        using var _d = f.H;

        f.H.ActingAsAdmin(f.WorkerId);
        await f.H.Attachments.UploadAsync(
            requestId: f.RequestId, taskId: null, uploaderId: f.WorkerId,
            File(), "problem.png", "image/png");

        await UploadToTaskAsync(f, f.WorkerId, "fixed.png", AttachmentKind.CompletionProof);

        var t = (await f.H.TaskQueries.GetAsync(f.TaskId)).Value!;

        Assert.Equal(new[] { "problem.png" }, t.Request!.Attachments.Select(a => a.FileName));
        Assert.Equal(new[] { "fixed.png" }, t.CompletionProof!.Select(a => a.FileName));
    }
}
