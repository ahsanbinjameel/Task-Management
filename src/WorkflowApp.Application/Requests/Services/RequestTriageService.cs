using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Requests.Services;

public interface IRequestTriageService
{
    /// <summary>Moves a submitted request into review, claiming it for the reviewer.</summary>
    Task<Result<RequestDetailDto>> StartReviewAsync(long requestId, long reviewerId, CancellationToken ct = default);

    /// <summary>Records the triage decision. Approval — and only approval — creates a task.</summary>
    Task<Result<TriageResult>> DecideAsync(
        long requestId, long reviewerId, TriageDecisionDto decision, CancellationToken ct = default);

    /// <summary>The requester answers an open clarification, returning the request to review.</summary>
    Task<Result<RequestDetailDto>> AnswerClarificationAsync(
        long clarificationId, long answeringUserId, string answer, CancellationToken ct = default);
}

/// <summary>What triage did, including the task id when a task was created.</summary>
public sealed record TriageResult(RequestStatus Status, long? CreatedTaskId, string? CreatedTaskNumber);

/// <summary>
/// The gate between "someone asked for something" and "the organisation is going to do it".
///
/// The rule this class exists to enforce: a request never becomes a task on its own. Five of the
/// six outcomes end the request's life without producing any work at all, which is what keeps
/// rejected and duplicate submissions out of worker queues.
/// </summary>
public sealed class RequestTriageService : IRequestTriageService
{
    private readonly IWorkflowDbContext _db;
    private readonly IRequestService _requests;
    private readonly ITaskCreationService _taskCreation;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RequestTriageService> _logger;

    public RequestTriageService(
        IWorkflowDbContext db,
        IRequestService requests,
        ITaskCreationService taskCreation,
        IAuditService audit,
        IDateTimeProvider clock,
        ILogger<RequestTriageService> logger)
    {
        _db = db;
        _requests = requests;
        _taskCreation = taskCreation;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<RequestDetailDto>> StartReviewAsync(
        long requestId, long reviewerId, CancellationToken ct = default)
    {
        var request = await _db.Requests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
            return Result<RequestDetailDto>.Failure(Error.NotFound("request.not_found", "Request not found."));

        if (request.Status == RequestStatus.InReview)
            return await _requests.GetAsync(requestId, ct);   // already claimed; idempotent

        if (request.Status is not (RequestStatus.Submitted or RequestStatus.ClarificationRequired))
            return Result<RequestDetailDto>.Failure(Error.Conflict(
                "request.not_reviewable", $"A request in {request.Status} is not awaiting review."));

        request.Status = RequestStatus.InReview;
        await _db.SaveChangesAsync(ct);

        return await _requests.GetAsync(requestId, ct);
    }

    public async Task<Result<TriageResult>> DecideAsync(
        long requestId, long reviewerId, TriageDecisionDto decision, CancellationToken ct = default)
    {
        var request = await _db.Requests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
            return Result<TriageResult>.Failure(Error.NotFound("request.not_found", "Request not found."));

        if (IsClosed(request.Status))
            return Result<TriageResult>.Failure(Error.Conflict(
                "request.already_decided", $"This request was already decided ({request.Status})."));

        if (RequiresReason(decision.Outcome) && string.IsNullOrWhiteSpace(decision.Reason))
            return Result<TriageResult>.Failure(Error.Validation(
                "triage.reason_required", $"A reason is required to {decision.Outcome}."));

        return decision.Outcome switch
        {
            TriageOutcome.Approve => await ApproveAsync(request, reviewerId, decision, ct),
            TriageOutcome.RequestClarification => await RequestClarificationAsync(request, reviewerId, decision.Reason!, ct),
            TriageOutcome.MarkDuplicate => await MarkDuplicateAsync(request, reviewerId, decision, ct),
            TriageOutcome.Reject => await CloseAsync(request, reviewerId, RequestStatus.Rejected, decision.Reason!, ct),
            TriageOutcome.Defer => await CloseAsync(request, reviewerId, RequestStatus.Deferred, decision.Reason!, ct),
            TriageOutcome.Escalate => await CloseAsync(request, reviewerId, RequestStatus.Escalated, decision.Reason, ct),
            _ => Result<TriageResult>.Failure(Error.Validation("triage.unknown_outcome", "Unknown triage outcome."))
        };
    }

    public async Task<Result<RequestDetailDto>> AnswerClarificationAsync(
        long clarificationId, long answeringUserId, string answer, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return Result<RequestDetailDto>.Failure(
                Error.Validation("clarification.answer_required", "An answer is required."));

        var clarification = await _db.RequestClarifications
            .Include(c => c.Request)
            .FirstOrDefaultAsync(c => c.Id == clarificationId, ct);

        if (clarification is null)
            return Result<RequestDetailDto>.Failure(
                Error.NotFound("clarification.not_found", "Clarification not found."));

        if (clarification.AnsweredAt is not null)
            return Result<RequestDetailDto>.Failure(
                Error.Conflict("clarification.already_answered", "This clarification was already answered."));

        if (clarification.Request.RequestedByUserId != answeringUserId)
            return Result<RequestDetailDto>.Failure(
                Error.Forbidden("clarification.not_owner", "Only the requester can answer this."));

        // The thread is append-only: the answer fills in the existing row, nothing is overwritten.
        clarification.Answer = answer.Trim();
        clarification.AnsweredByUserId = answeringUserId;
        clarification.AnsweredAt = _clock.UtcNow;

        // Answering returns the request to the review queue. It must not skip ahead to Approved —
        // the reviewer still has to look at the answer.
        clarification.Request.Status = RequestStatus.Submitted;

        await _db.SaveChangesAsync(ct);
        return await _requests.GetAsync(clarification.RequestId, ct);
    }

    // --- outcomes ------------------------------------------------------------------------

    private async Task<Result<TriageResult>> ApproveAsync(
        Request request, long reviewerId, TriageDecisionDto decision, CancellationToken ct)
    {
        // An unanswered question means the reviewer does not yet have what they asked for.
        if (await _db.RequestClarifications.AnyAsync(
                c => c.RequestId == request.Id && c.AnsweredAt == null, ct))
        {
            return Result<TriageResult>.Failure(Error.Conflict(
                "request.clarification_pending",
                "This request has an unanswered clarification. Resolve it before approving."));
        }

        // The requester's urgency is advisory; the approved priority is what schedules the work.
        var priority = decision.ApprovedPriority ?? MapUrgency(request.RequestedUrgency);

        var task = await _taskCreation.CreateFromRequestAsync(
            request, reviewerId, priority, decision.EstimatedEffortHours, decision.DueDate,
            decision.AcceptanceCriteria, ct);

        request.Status = RequestStatus.Approved;
        request.GeneratedTaskId = task.Id;

        _audit.Record(
            AuditActions.RequestApproved,
            actorUserId: reviewerId,
            entityType: nameof(Request),
            entityId: request.Id,
            newValues: new { TaskId = task.Id, task.TaskNumber, Priority = priority.ToString() });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Request {RequestNumber} approved by {ReviewerId}; created task {TaskNumber}",
            request.RequestNumber, reviewerId, task.TaskNumber);

        return Result<TriageResult>.Success(new TriageResult(RequestStatus.Approved, task.Id, task.TaskNumber));
    }

    private async Task<Result<TriageResult>> RequestClarificationAsync(
        Request request, long reviewerId, string question, CancellationToken ct)
    {
        _db.RequestClarifications.Add(new RequestClarification
        {
            RequestId = request.Id,
            AskedByUserId = reviewerId,
            Question = question.Trim(),
            AskedAt = _clock.UtcNow
        });

        request.Status = RequestStatus.ClarificationRequired;

        await _db.SaveChangesAsync(ct);
        return Result<TriageResult>.Success(new TriageResult(RequestStatus.ClarificationRequired, null, null));
    }

    private async Task<Result<TriageResult>> MarkDuplicateAsync(
        Request request, long reviewerId, TriageDecisionDto decision, CancellationToken ct)
    {
        if (decision.DuplicateOfRequestId is not { } originalId)
            return Result<TriageResult>.Failure(Error.Validation(
                "triage.duplicate_target_required", "Specify which request this duplicates."));

        if (originalId == request.Id)
            return Result<TriageResult>.Failure(Error.Validation(
                "triage.duplicate_self", "A request cannot duplicate itself."));

        if (!await _db.Requests.AnyAsync(r => r.Id == originalId, ct))
            return Result<TriageResult>.Failure(Error.NotFound(
                "triage.duplicate_target_not_found", "The request it duplicates was not found."));

        request.RelatedRequestId = originalId;
        request.Status = RequestStatus.Duplicate;

        _audit.Record(
            AuditActions.RequestDuplicated,
            actorUserId: reviewerId,
            entityType: nameof(Request),
            entityId: request.Id,
            newValues: new { DuplicateOf = originalId, Reason = decision.Reason });

        await _db.SaveChangesAsync(ct);

        // No task: a duplicate must never reach a worker queue.
        return Result<TriageResult>.Success(new TriageResult(RequestStatus.Duplicate, null, null));
    }

    private async Task<Result<TriageResult>> CloseAsync(
        Request request, long reviewerId, RequestStatus status, string? reason, CancellationToken ct)
    {
        request.Status = status;

        // The decision and its reason are recorded on the request's own thread so the requester can
        // see why, not just that.
        _db.RequestClarifications.Add(new RequestClarification
        {
            RequestId = request.Id,
            AskedByUserId = reviewerId,
            Question = $"[{status}] {reason ?? "(no reason given)"}",
            AskedAt = _clock.UtcNow,
            // Pre-answered: it is a statement of outcome, not a question awaiting a reply.
            AnsweredByUserId = reviewerId,
            Answer = "(triage decision)",
            AnsweredAt = _clock.UtcNow
        });

        _audit.Record(
            status == RequestStatus.Rejected ? AuditActions.RequestRejected : AuditActions.RequestTriaged,
            actorUserId: reviewerId,
            entityType: nameof(Request),
            entityId: request.Id,
            newValues: new { Status = status.ToString(), Reason = reason });

        await _db.SaveChangesAsync(ct);
        return Result<TriageResult>.Success(new TriageResult(status, null, null));
    }

    // --- rules ---------------------------------------------------------------------------

    private static bool RequiresReason(TriageOutcome outcome) => outcome is
        TriageOutcome.Reject or TriageOutcome.RequestClarification or
        TriageOutcome.MarkDuplicate or TriageOutcome.Defer;

    private static bool IsClosed(RequestStatus status) => status is
        RequestStatus.Approved or RequestStatus.Rejected or RequestStatus.Duplicate;

    /// <summary>Default mapping from what was asked for to what was approved.</summary>
    private static Priority MapUrgency(RequestedUrgency urgency) => urgency switch
    {
        RequestedUrgency.Critical => Priority.Critical,
        RequestedUrgency.High => Priority.High,
        RequestedUrgency.Low => Priority.Low,
        _ => Priority.Normal
    };
}
