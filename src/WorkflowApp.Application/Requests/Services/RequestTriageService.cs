using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Notifications;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Application.Verifications.Dtos;
using WorkflowApp.Application.Verifications.Services;
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

/// <summary>
/// What triage did, including the task id when a task was created — and the verification id when
/// one was raised instead. Never both: those are the two ends of the decision.
/// </summary>
public sealed record TriageResult(
    RequestStatus Status,
    long? CreatedTaskId,
    string? CreatedTaskNumber,
    long? VerificationId = null,
    string? VerificationNumber = null);

/// <summary>
/// The gate between "someone asked for something" and "the organisation is going to do it".
///
/// The rule this class exists to enforce: a request never becomes a task on its own. Six of the
/// seven outcomes produce no work at all, which is what keeps rejected, duplicate and
/// not-yet-understood submissions out of worker queues.
///
/// <para>
/// <see cref="TriageOutcome.SendForVerification"/> is the newest of those six and the only one that
/// does not end the request's life: it hands the request to a checker, who establishes whether
/// there is anything to build, and the request comes back here for a real decision with their
/// findings attached. It creates a <c>Verification</c>, never a task — a confirmed problem still
/// has to be approved explicitly, so <see cref="ITaskCreationService"/> keeps its monopoly.
/// </para>
/// </summary>
public sealed class RequestTriageService : IRequestTriageService
{
    private readonly IWorkflowDbContext _db;
    private readonly IRequestService _requests;
    private readonly ITaskCreationService _taskCreation;
    private readonly IVerificationService _verifications;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly ILookupService _lookups;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RequestTriageService> _logger;

    public RequestTriageService(
        IWorkflowDbContext db,
        IRequestService requests,
        ITaskCreationService taskCreation,
        IVerificationService verifications,
        IAuditService audit,
        INotificationService notifications,
        ILookupService lookups,
        IDateTimeProvider clock,
        ILogger<RequestTriageService> logger)
    {
        _db = db;
        _requests = requests;
        _taskCreation = taskCreation;
        _verifications = verifications;
        _audit = audit;
        _notifications = notifications;
        _lookups = lookups;
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
            return await _requests.GetAsync(requestId, ct: ct);   // already claimed; idempotent

        if (request.Status is not (RequestStatus.Submitted or RequestStatus.ClarificationRequired))
            return Result<RequestDetailDto>.Failure(Error.Conflict(
                "request.not_reviewable", $"A request in {request.Status} is not awaiting review."));

        request.Status = RequestStatus.InReview;
        await _db.SaveChangesAsync(ct);

        return await _requests.GetAsync(requestId, ct: ct);
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

        // Nothing gets decided while somebody is still checking. Same reasoning as the unanswered
        // clarification below: the reviewer asked a question and does not yet have the answer.
        //
        // Applied to every decisive outcome rather than only to approval, because the loose end is
        // the same either way — a checker who submits findings against a request that was rejected
        // underneath them has done the work for nothing, and the verification is left pointing at a
        // decision it played no part in. Asking for a clarification is exempt: that is a question,
        // not a decision, and the two can reasonably run at once.
        if (decision.Outcome is not (TriageOutcome.RequestClarification or TriageOutcome.SendForVerification)
            && await _verifications.HasOpenForRequestAsync(request.Id, ct))
        {
            return Result<TriageResult>.Failure(Error.Conflict(
                "request.verification_pending",
                "This request is still being checked. Wait for the findings, or call the check off."));
        }

        return decision.Outcome switch
        {
            TriageOutcome.Approve => await ApproveAsync(request, reviewerId, decision, ct),
            TriageOutcome.RequestClarification => await RequestClarificationAsync(request, reviewerId, decision.Reason!, ct),
            TriageOutcome.MarkDuplicate => await MarkDuplicateAsync(request, reviewerId, decision, ct),
            TriageOutcome.Reject => await CloseAsync(request, reviewerId, RequestStatus.Rejected, decision.Reason!, ct),
            TriageOutcome.Defer => await CloseAsync(request, reviewerId, RequestStatus.Deferred, decision.Reason!, ct),
            TriageOutcome.Escalate => await CloseAsync(request, reviewerId, RequestStatus.Escalated, decision.Reason, ct),
            TriageOutcome.SendForVerification => await SendForVerificationAsync(request, reviewerId, decision, ct),
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

        // Back to the reviewer who asked. Addressed to them personally rather than the whole review
        // group: they are waiting on this specific answer.
        _notifications.RaiseFor(
            new long?[] { clarification.AskedByUserId }, answeringUserId,
            $"{clarification.Request.RequestNumber}: the requester has replied",
            answer.Trim(), NotificationService.LinkRequest, clarification.RequestId);

        await _db.SaveChangesAsync(ct);
        return await _requests.GetAsync(clarification.RequestId, ct: ct);
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

        // Corrections land on the request, and the task inherits from it a line later. Writing to
        // both separately is how the same task ends up filed under two different clients.
        if (!string.IsNullOrWhiteSpace(decision.ClientName))
            request.ClientId = await _lookups.ResolveClientAsync(decision.ClientName, ct);

        // The other axis: where in the product this is (PRODUCT-CORE §5). Written to the request
        // for the same reason the client is, and inherited by the task a few lines down.
        //
        // Each level clears the ones below it. Moving a request from Sales to Accounts while it
        // still points at the Delivery Order form would leave a combination that does not exist,
        // and a report grouped by module would then count it under Accounts and name a Sales form.
        if (decision.ModuleId is { } moduleId && moduleId != request.ModuleId)
        {
            request.ModuleId = moduleId;
            request.FormId = null;
            request.FormSurfaceId = null;
        }

        if (decision.FormId is { } formId && formId != request.FormId)
        {
            request.FormId = formId;
            request.FormSurfaceId = null;
        }

        if (decision.FormSurfaceId is { } surfaceId)
            request.FormSurfaceId = surfaceId;

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

        // The requester has been waiting on this decision; the coordinators now have work to place.
        _notifications.RaiseFor(
            new long?[] { request.RequestedByUserId }, reviewerId,
            $"Your request {request.RequestNumber} was approved",
            $"It is now task {task.TaskNumber}: {task.Title}",
            NotificationService.LinkRequest, request.Id);

        await _notifications.RaiseForPermissionAsync(
            Permissions.TaskAssign, reviewerId,
            $"{task.TaskNumber} is Ready For Assignment",
            task.Title, NotificationService.LinkTask, task.Id, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Request {RequestNumber} approved by {ReviewerId}; created task {TaskNumber}",
            request.RequestNumber, reviewerId, task.TaskNumber);

        return Result<TriageResult>.Success(new TriageResult(RequestStatus.Approved, task.Id, task.TaskNumber));
    }

    /// <summary>
    /// Route the request to a checker rather than deciding it.
    ///
    /// The reviewer's honest position is "I cannot tell from this whether there is anything to
    /// build". Before this existed the only ways forward were to guess, to bounce it back to the
    /// requester who already said what they knew, or to approve it into a task so that somebody
    /// would look — which commits the organisation to work before anyone has established there is
    /// any. This is the fourth answer, and it creates no work.
    /// </summary>
    private async Task<Result<TriageResult>> SendForVerificationAsync(
        Request request, long reviewerId, TriageDecisionDto decision, CancellationToken ct)
    {
        if (decision.Verification is not { } details)
            return Result<TriageResult>.Failure(Error.Validation(
                "triage.verification_details_required", "Say what needs checking, and by whom."));

        // One at a time. A second open check on the same request would leave two people
        // investigating the same thing and the request waiting on whichever answered last.
        if (await _verifications.HasOpenForRequestAsync(request.Id, ct))
            return Result<TriageResult>.Failure(Error.Conflict(
                "request.verification_pending", "This request is already being checked."));

        var raised = await _verifications.RaiseForRequestAsync(request, reviewerId, details, ct);
        if (!raised.IsSuccess) return Result<TriageResult>.Failure(raised.Error!);

        var verification = raised.Value!;

        request.Status = RequestStatus.UnderVerification;

        _db.RequestActivities.Add(new RequestActivity
        {
            RequestId = request.Id,
            Type = ActivityType.VerificationRequested,
            ActorUserId = reviewerId,
            OccurredAt = _clock.UtcNow,
            Description = $"Sent for checking as {verification.VerificationNumber}"
        });

        _audit.Record(
            AuditActions.RequestTriaged,
            actorUserId: reviewerId,
            entityType: nameof(Request),
            entityId: request.Id,
            newValues: new
            {
                Status = RequestStatus.UnderVerification.ToString(),
                verification.VerificationNumber,
                VerificationId = verification.Id
            });

        // The requester is told in their own words — "being checked" — not that a Verification
        // aggregate now exists. See StatusViews: this folds into "Being Checked" for them.
        _notifications.RaiseFor(
            new long?[] { request.RequestedByUserId }, reviewerId,
            $"Your request {request.RequestNumber} is being checked",
            "Someone is looking into it before a decision is made.",
            NotificationService.LinkRequest, request.Id);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Request {RequestNumber} sent for verification as {VerificationNumber} by {ReviewerId}",
            request.RequestNumber, verification.VerificationNumber, reviewerId);

        return Result<TriageResult>.Success(new TriageResult(
            RequestStatus.UnderVerification, null, null,
            verification.Id, verification.VerificationNumber));
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

        // Nothing moves until they answer, so this one has to reach them.
        _notifications.RaiseFor(
            new long?[] { request.RequestedByUserId }, reviewerId,
            $"More information needed on {request.RequestNumber}",
            question.Trim(), NotificationService.LinkRequest, request.Id);

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

        _notifications.RaiseFor(
            new long?[] { request.RequestedByUserId }, reviewerId,
            status == RequestStatus.Rejected
                ? $"Your request {request.RequestNumber} was not approved"
                : $"Your request {request.RequestNumber} was {status.ToString().ToLowerInvariant()}",
            reason, NotificationService.LinkRequest, request.Id);

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
