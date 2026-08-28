using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Notifications;
using WorkflowApp.Application.Requests.Services;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Services;

public interface IQCService
{
    /// <summary>Completed work waiting for a reviewer.</summary>
    Task<PagedResult<TaskSummaryDto>> QueueAsync(PageQuery page, CancellationToken ct = default);

    /// <summary>Claims the task for review and moves it CompletedReadyForQC to QCReview.</summary>
    Task<Result<TaskDetailDto>> StartReviewAsync(long taskId, long reviewerId, CancellationToken ct = default);

    /// <summary>
    /// Records one QC attempt and moves the task accordingly. This is the only way into QCPassed
    /// and QCFailedRework, so every one of those states has a review record behind it.
    /// </summary>
    Task<Result<TaskDetailDto>> SubmitAsync(
        long taskId, long reviewerId, SubmitQCReviewDto request, CancellationToken ct = default);

    /// <summary>The task's acceptance criteria with the most recent verdict against each.</summary>
    Task<Result<AcceptanceCriteriaDto>> CriteriaAsync(long taskId, CancellationToken ct = default);

    /// <summary>Every QC attempt, oldest first. Append-only: a later pass never erases a failure.</summary>
    Task<IReadOnlyList<QCReviewDto>> HistoryAsync(long taskId, CancellationToken ct = default);
}

/// <summary>
/// Quality control. Work arrives here when the assignee completes it and leaves either as passed or
/// back into rework, never straight to closed.
///
/// Two rules shape this class. Every QC decision is an <b>attempt</b>, numbered and retained, so a
/// task that failed twice before passing still says so. And a <b>pass is gated on the acceptance
/// criteria</b>: if the task declares criteria, the reviewer has to answer all of them and every one
/// must be met, which is what stops "looks fine to me" from becoming a closure.
/// </summary>
public sealed class QCService : IQCService
{
    private readonly IWorkflowDbContext _db;
    private readonly IAttachmentService _attachments;
    private readonly ITaskQueryService _queries;
    private readonly IActivityLogger _activity;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<QCService> _logger;

    public QCService(
        IWorkflowDbContext db,
        IAttachmentService attachments,
        ITaskQueryService queries,
        IActivityLogger activity,
        IAuditService audit,
        INotificationService notifications,
        IDateTimeProvider clock,
        ILogger<QCService> logger)
    {
        _db = db;
        _attachments = attachments;
        _queries = queries;
        _activity = activity;
        _audit = audit;
        _notifications = notifications;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Everything a checker has to deal with: work handed over and not yet claimed, and work
    /// already being checked.
    ///
    /// This used to filter on <see cref="WorkTaskStatus.CompletedReadyForQC"/> alone, which meant a
    /// task disappeared from this page the instant somebody claimed it — while every other screen
    /// in the app went on saying "Being checked", because that label covers
    /// <c>CompletedReadyForQC</c> <em>and</em> <c>QCReview</c>. Two screens contradicted each other
    /// and the work in hand was the part that vanished.
    ///
    /// So the queue is now driven by the same <see cref="StatusViews"/> entry that produces the
    /// label. That is the fix rather than adding a second status to the filter: one table decides
    /// what "being checked" means, and a queue built from it cannot drift from the word again.
    /// </summary>
    public Task<PagedResult<TaskSummaryDto>> QueueAsync(PageQuery page, CancellationToken ct = default) =>
        _queries.ListAsync(
            new TaskQuery { View = "checking", Audience = StatusAudience.Coordinator }, page, ct);

    public async Task<Result<TaskDetailDto>> StartReviewAsync(
        long taskId, long reviewerId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        // Already under review by the same person: a no-op, so a refreshed page is harmless.
        if (task.Status == WorkTaskStatus.QCReview && task.QCUserId == reviewerId)
            return await _queries.GetAsync(taskId, ct);

        // Who may review is answered before whether it is reviewable yet, so a second reviewer is
        // told the task is taken rather than being told it is not ready.
        if (Ineligible(task, reviewerId) is { } blocked)
            return Result<TaskDetailDto>.Failure(blocked);

        if (task.Status != WorkTaskStatus.CompletedReadyForQC)
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "qc.not_ready",
                $"A task in {TaskWorkflowService.Humanize(task.Status)} is not waiting for QC."));

        // Whoever picks it up owns it, unless a coordinator nominated someone in advance.
        task.QCUserId = reviewerId;

        TaskStatusJournal.Write(
            _db, _activity, task, WorkTaskStatus.QCReview, reviewerId, now,
            reason: null, ActivityType.QCStarted, $"QC review started on {task.TaskNumber}.");

        await _db.SaveChangesAsync(ct);
        return await _queries.GetAsync(taskId, ct);
    }

    public async Task<Result<TaskDetailDto>> SubmitAsync(
        long taskId, long reviewerId, SubmitQCReviewDto request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (task.Status != WorkTaskStatus.QCReview)
            return Result<TaskDetailDto>.Failure(Error.Conflict(
                "qc.not_under_review",
                $"A task in {TaskWorkflowService.Humanize(task.Status)} has no QC review in progress."));

        if (Ineligible(task, reviewerId) is { } blocked)
            return Result<TaskDetailDto>.Failure(blocked);

        // Anything other than a clean pass is a judgement the assignee has to act on, so it has to
        // say why. Same reason-required rule that governs reject, pause and block.
        if (request.Result != QCResult.Passed && string.IsNullOrWhiteSpace(request.Comments))
            return Result<TaskDetailDto>.Failure(Error.Validation(
                "qc.comments_required", "A failed or queried QC review must explain what is wrong."));

        var evaluation = Evaluate(task, request);
        if (evaluation.IsFailure)
            return Result<TaskDetailDto>.Failure(evaluation.Error!);

        var criteria = evaluation.Value!;

        var highest = await _db.QCReviews
            .Where(q => q.TaskId == taskId)
            .Select(q => (int?)q.AttemptNumber)
            .MaxAsync(ct);

        var attemptNumber = (highest ?? 0) + 1;

        var review = new QCReview
        {
            TaskId = task.Id,
            ReviewerUserId = reviewerId,
            ReviewedAt = now,
            Result = request.Result,
            Comments = request.Comments,
            Environment = request.Environment,
            BuildVersion = request.BuildVersion,
            AttemptNumber = attemptNumber,
            AcceptanceCriteriaResults = criteria.Count == 0 ? null : AcceptanceCriteria.Serialize(criteria)
        };

        _db.QCReviews.Add(review);

        // The attempt's id has to exist before the evidence can point at it. Saved here rather than
        // at the end so the claim below has something to write; the status change, the notification
        // and the audit row still commit together in the save that follows.
        await _db.SaveChangesAsync(ct);

        // Evidence is uploaded before the verdict — the attempt does not exist until this moment —
        // so it is staged unclaimed and tied to the attempt here. A verdict that was refused
        // earlier leaves the files staged for the retry rather than stranding them.
        await _attachments.ClaimQCEvidenceAsync(task.Id, reviewerId, review.Id, ct);

        switch (request.Result)
        {
            case QCResult.Passed:
                TaskStatusJournal.Write(
                    _db, _activity, task, WorkTaskStatus.QCPassed, reviewerId, now,
                    request.Comments, ActivityType.QCPassed,
                    $"QC attempt {attemptNumber} passed on {task.TaskNumber}.");
                break;

            case QCResult.Failed:
                // Rework, not closure. The assignee picks it back up from QCFailedRework.
                TaskStatusJournal.Write(
                    _db, _activity, task, WorkTaskStatus.QCFailedRework, reviewerId, now,
                    request.Comments, ActivityType.QCFailed,
                    $"QC attempt {attemptNumber} failed on {task.TaskNumber}: {request.Comments}");
                break;

            case QCResult.ClarificationRequired:
                // The reviewer has a question rather than a verdict, so the task stays under review.
                // There is no lifecycle state for "QC is waiting on an answer", and inventing a
                // transition here would put the map and the code out of step.
                _db.TaskActivities.Add(new TaskActivity
                {
                    TaskId = task.Id,
                    Type = ActivityType.ClarificationRequested,
                    ActorUserId = reviewerId,
                    OccurredAt = now,
                    Description = $"QC attempt {attemptNumber} raised a query on {task.TaskNumber}: {request.Comments}"
                });
                break;
        }

        // A verdict is the assignee's cue to act, or to stop waiting.
        _notifications.RaiseFor(
            new[] { task.PrimaryAssigneeUserId }, reviewerId,
            request.Result switch
            {
                QCResult.Passed => $"{task.TaskNumber} passed QC",
                QCResult.Failed => $"{task.TaskNumber} failed QC and needs rework",
                _ => $"QC has a question on {task.TaskNumber}"
            },
            request.Comments, NotificationService.LinkTask, task.Id);

        _audit.Record(
            request.Result == QCResult.Passed ? AuditActions.QCPassed : AuditActions.QCFailed,
            actorUserId: reviewerId,
            entityType: nameof(WorkTask),
            entityId: task.Id,
            newValues: new
            {
                Attempt = attemptNumber,
                Result = request.Result.ToString(),
                request.Comments,
                request.Environment,
                request.BuildVersion
            });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "QC attempt {Attempt} on {TaskNumber} by user {ReviewerId}: {Result}",
            attemptNumber, task.TaskNumber, reviewerId, request.Result);

        return await _queries.GetAsync(taskId, ct);
    }

    public async Task<Result<AcceptanceCriteriaDto>> CriteriaAsync(long taskId, CancellationToken ct = default)
    {
        var task = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<AcceptanceCriteriaDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        var latest = await LatestReviewAsync(taskId, ct);
        var criteria = MergeVerdicts(task.AcceptanceCriteria, latest?.AcceptanceCriteriaResults);

        return Result<AcceptanceCriteriaDto>.Success(new AcceptanceCriteriaDto(
            taskId, criteria, latest?.AttemptNumber, latest?.ReviewedAt));
    }

    public async Task<IReadOnlyList<QCReviewDto>> HistoryAsync(long taskId, CancellationToken ct = default)
    {
        var reviews = await _db.QCReviews.AsNoTracking()
            .Where(q => q.TaskId == taskId)
            .OrderBy(q => q.AttemptNumber)
            .ToListAsync(ct);

        if (reviews.Count == 0) return Array.Empty<QCReviewDto>();

        var reviewerIds = reviews.Select(q => q.ReviewerUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => reviewerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        // One query for every attempt's evidence rather than one per attempt.
        var reviewIds = reviews.Select(r => r.Id).ToList();
        var evidence = (await _db.Attachments.AsNoTracking()
                .Where(a => a.QCReviewId != null && reviewIds.Contains(a.QCReviewId!.Value))
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct))
            .GroupBy(a => a.QCReviewId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AttachmentDto>)g.Select(a => new AttachmentDto(
                    a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes,
                    a.UploadedByUserId, a.CreatedAt)).ToList());

        return reviews.Select(q => new QCReviewDto(
            q.Id, q.TaskId, q.AttemptNumber, q.ReviewerUserId,
            names.TryGetValue(q.ReviewerUserId, out var name) ? name : null,
            q.ReviewedAt, q.Result, q.Comments, q.Environment, q.BuildVersion,
            AcceptanceCriteria.Deserialize(q.AcceptanceCriteriaResults),
            evidence.TryGetValue(q.Id, out var files) ? files : Array.Empty<AttachmentDto>())).ToList();
    }

    // --- helpers -------------------------------------------------------------------------

    internal Task<QCReview?> LatestReviewAsync(long taskId, CancellationToken ct) =>
        _db.QCReviews.AsNoTracking()
            .Where(q => q.TaskId == taskId)
            .OrderByDescending(q => q.AttemptNumber)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Pairs the task's current criteria text with a stored evaluation. A verdict only carries over
    /// when the criterion text still matches, so editing the criteria after QC ran shows those items
    /// as unevaluated rather than silently inheriting a stale pass.
    /// </summary>
    internal static IReadOnlyList<AcceptanceCriterionDto> MergeVerdicts(
        string? criteriaText, string? storedEvaluation)
    {
        var verdicts = AcceptanceCriteria.Deserialize(storedEvaluation)
            .GroupBy(c => c.Index)
            .ToDictionary(g => g.Key, g => g.First());

        return AcceptanceCriteria.Parse(criteriaText)
            .Select((text, index) => verdicts.TryGetValue(index, out var verdict) && verdict.Text == text
                ? new AcceptanceCriterionDto(index, text, verdict.Met, verdict.Note)
                : new AcceptanceCriterionDto(index, text, null, null))
            .ToList();
    }

    /// <summary>
    /// Segregation of duties: nobody signs off their own work, and a nominated QC owner cannot be
    /// elbowed aside by another reviewer mid-review.
    /// </summary>
    private static Error? Ineligible(WorkTask task, long reviewerId)
    {
        if (task.PrimaryAssigneeUserId == reviewerId)
            return Error.Forbidden("qc.reviewer_is_assignee", "The assignee cannot QC their own work.");

        if (task.QCUserId is { } owner && owner != reviewerId)
            return Error.Forbidden("qc.not_qc_owner", "This task is assigned to a different QC reviewer.");

        return null;
    }

    /// <summary>
    /// Pairs the submitted verdicts with the task's criteria. A pass has to answer every criterion
    /// affirmatively; a failure may leave them unanswered, because the comments carry the reason.
    /// </summary>
    private static Result<IReadOnlyList<AcceptanceCriterionDto>> Evaluate(WorkTask task, SubmitQCReviewDto request)
    {
        var criteria = AcceptanceCriteria.Parse(task.AcceptanceCriteria);

        var outOfRange = request.Criteria.FirstOrDefault(v => v.Index < 0 || v.Index >= criteria.Count);
        if (outOfRange is not null)
            return Result<IReadOnlyList<AcceptanceCriterionDto>>.Failure(Error.Validation(
                "qc.criterion_unknown",
                $"Criterion {outOfRange.Index} does not exist on this task. The acceptance criteria may have changed since the page was loaded."));

        var byIndex = request.Criteria
            .GroupBy(v => v.Index)
            .ToDictionary(g => g.Key, g => g.Last());

        if (request.Result == QCResult.Passed && criteria.Count > 0)
        {
            // "Not applicable" (Met == null) is an answer. Only a missing entry is unanswered.
            var unanswered = Enumerable.Range(0, criteria.Count).Where(i => !byIndex.ContainsKey(i)).ToList();
            if (unanswered.Count > 0)
                return Result<IReadOnlyList<AcceptanceCriterionDto>>.Failure(Error.Validation(
                    "qc.criteria_incomplete",
                    $"Please mark every item as Pass, Fail or Not applicable before passing this check. Still to answer: {Positions(unanswered)}."));

            var failed = byIndex.Values.Where(v => v.Met == false).Select(v => v.Index).OrderBy(i => i).ToList();
            if (failed.Count > 0)
                return Result<IReadOnlyList<AcceptanceCriterionDto>>.Failure(Error.Validation(
                    "qc.criteria_unmet",
                    $"This check cannot pass while item {Positions(failed)} is marked as failed. Mark the whole check as Needs fixing instead, or change that item to Pass."));
        }

        var results = criteria
            .Select((text, index) => byIndex.TryGetValue(index, out var verdict)
                ? new AcceptanceCriterionDto(index, text, verdict.Met, verdict.Note)
                : new AcceptanceCriterionDto(index, text, null, null))
            .ToList();

        return Result<IReadOnlyList<AcceptanceCriterionDto>>.Success(results);
    }

    /// <summary>
    /// Renders zero-based criterion indexes as the 1-based positions the reviewer actually sees,
    /// joined the way a person would say them ("1, 2 and 5"). Error text is read by non-technical
    /// users, so it names the row on screen rather than the array offset.
    /// </summary>
    private static string Positions(IReadOnlyList<int> zeroBased)
    {
        var numbers = zeroBased.Select(i => (i + 1).ToString()).ToList();

        return numbers.Count switch
        {
            0 => string.Empty,
            1 => numbers[0],
            _ => $"{string.Join(", ", numbers.Take(numbers.Count - 1))} and {numbers[^1]}",
        };
    }
}
