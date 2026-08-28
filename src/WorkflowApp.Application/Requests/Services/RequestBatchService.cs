using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Notifications;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Requests.Services;

public interface IRequestBatchService
{
    Task<Result<RequestBatchDetailDto>> CreateAsync(
        long requesterId, CreateRequestBatchDto dto, CancellationToken ct = default);

    Task<Result<RequestBatchDetailDto>> GetAsync(long batchId, CancellationToken ct = default);

    /// <summary>Batches with at least one item still awaiting a decision, oldest first.</summary>
    Task<PagedResult<RequestBatchSummaryDto>> ReviewQueueAsync(PageQuery page, CancellationToken ct = default);

    /// <summary>The caller's own batches, newest first.</summary>
    Task<PagedResult<RequestBatchSummaryDto>> MineAsync(
        long requesterId, PageQuery page, CancellationToken ct = default);

    /// <summary>
    /// Approve several items of a batch as one piece of work. Every chosen item is approved in its
    /// own right; they simply end up pointing at the same task.
    /// </summary>
    Task<Result<TriageResult>> ApproveTogetherAsync(
        long batchId, long reviewerId, ApproveTogetherDto decision, CancellationToken ct = default);
}

/// <summary>
/// Several things asked for at once.
///
/// The batch is a wrapper around ordinary requests and nothing more. Each item is a full
/// <see cref="Request"/> with its own number, its own status and its own triage decision, so
/// everything that already works — the review queue, clarifications, editing before approval,
/// notifications, the requester's progress view — works on a batch item without knowing batches
/// exist. That is the whole design: batching is an intake convenience, not a second workflow.
///
/// <para>
/// Two things are genuinely new. Items are <b>created together</b>, sharing a client, a note and a
/// set of files, so nobody retypes the same context eight times. And a reviewer may <b>fold several
/// approved items into one task</b>, because "these three are the same underlying fix" is a
/// judgement only a person can make.
/// </para>
///
/// <para>
/// Folding needs no new schema. <c>Request.GeneratedTaskId</c> already answers "which task did my
/// request become", and several requests may answer with the same task;
/// <c>WorkTask.RequestId</c> answers the other direction with the item the task was raised from.
/// A join table would have been a second place to keep the same fact.
/// </para>
/// </summary>
public sealed class RequestBatchService : IRequestBatchService
{
    private readonly IWorkflowDbContext _db;
    private readonly INumberGenerator _numbers;
    private readonly INotificationService _notifications;
    private readonly ILookupService _lookups;
    private readonly ITaskCreationService _taskCreation;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RequestBatchService> _logger;

    public RequestBatchService(
        IWorkflowDbContext db,
        INumberGenerator numbers,
        INotificationService notifications,
        ILookupService lookups,
        ITaskCreationService taskCreation,
        IAuditService audit,
        IDateTimeProvider clock,
        ILogger<RequestBatchService> logger)
    {
        _db = db;
        _numbers = numbers;
        _notifications = notifications;
        _lookups = lookups;
        _taskCreation = taskCreation;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// What to call the submission when the requester was not asked (PRODUCT-CORE §8).
    ///
    /// One point is its own name. Several are named after the first, because that is what the
    /// person was thinking about when they started, and a count so a list of submissions reads as
    /// something other than a column of near-identical sentences.
    /// </summary>
    private static string BatchTitle(CreateRequestBatchDto dto, IReadOnlyList<BatchItemDto> items)
    {
        if (!string.IsNullOrWhiteSpace(dto.Title)) return dto.Title.Trim();

        var first = items[0].Title.Trim();
        if (items.Count == 1) return Shorten(first, 300);

        var suffix = $" (+{items.Count - 1} more)";
        return Shorten(first, 300 - suffix.Length) + suffix;
    }

    private static string Shorten(string text, int limit) =>
        text.Length <= limit ? text : text[..(limit - 1)].TrimEnd() + "\u2026";

    public async Task<Result<RequestBatchDetailDto>> CreateAsync(
        long requesterId, CreateRequestBatchDto dto, CancellationToken ct = default)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == requesterId, ct))
            return Result<RequestBatchDetailDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        var items = dto.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Title) || !string.IsNullOrWhiteSpace(i.Description))
            .ToList();

        // Blank rows are a fact of any repeatable form — somebody clicks "add another" and changes
        // their mind. Dropping them is kinder than refusing the whole submission, but a batch of
        // nothing but blanks is a mistake worth reporting.
        if (items.Count == 0)
            return Result<RequestBatchDetailDto>.Failure(Error.Validation(
                "batch.no_items", "Add at least one thing you are asking for."));

        // A point needs something said about it, and nothing else (PRODUCT-CORE §8). The
        // description used to be demanded alongside the title, which on the fast intake form would
        // mean typing the same sentence twice.
        var missing = items.FindIndex(i => string.IsNullOrWhiteSpace(i.Title));

        if (missing >= 0)
            return Result<RequestBatchDetailDto>.Failure(Error.Validation(
                "batch.item_incomplete",
                $"Item {missing + 1} needs something said about it."));

        var now = _clock.UtcNow;
        var clientId = await _lookups.ResolveClientAsync(dto.ClientName, ct);

        var batch = new RequestBatch
        {
            BatchNumber = await _numbers.NextAsync(NumberSequences.Batch, NumberSequences.BatchPrefix, ct),
            Title = BatchTitle(dto, items),
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            ClientId = clientId,
            RequestedByUserId = requesterId,
            RequestedAt = now,
        };

        _db.RequestBatches.Add(batch);

        var created = new List<Request>(items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            var request = new Request
            {
                RequestNumber = await _numbers.NextAsync(
                    NumberSequences.Request, NumberSequences.RequestPrefix, ct),
                Title = item.Title.Trim(),

                // The point in full, falling back to what was said when nothing longer was
                // written. A request with an empty description reads as a blank panel on every
                // screen that shows one.
                Description = string.IsNullOrWhiteSpace(item.Description)
                    ? item.Title.Trim()
                    : item.Description.Trim(),

                Type = item.Type,
                RequestedUrgency = item.RequestedUrgency,

                // The product axis, shared and copied for the same reason the client is: an item
                // refiled at triage must not drag its siblings with it.
                ModuleId = dto.ModuleId,
                FormId = dto.FormId,

                // Copied, not read through the batch. An item corrected at triage must not drag its
                // siblings with it — which is exactly what happens when eight month-end problems
                // turn out to belong to two different clients.
                ClientId = clientId,

                TargetDate = item.TargetDate,
                RequestedByUserId = requesterId,
                RequestedAt = now,
                Status = RequestStatus.Submitted,

                Batch = batch,
                OrdinalInBatch = i + 1,
            };

            _db.Requests.Add(request);
            created.Add(request);
        }

        // The ids have to exist before the history rows can reference them — the same two-step
        // TaskCreationService uses. The batch and its items commit together either way, so a batch
        // can never exist with no items behind it.
        await _db.SaveChangesAsync(ct);

        foreach (var request in created)
        {
            _db.RequestActivities.Add(new RequestActivity
            {
                RequestId = request.Id,
                Type = ActivityType.RequestSubmitted,
                ActorUserId = requesterId,
                OccurredAt = now,
                Description = $"Submitted as item {request.OrdinalInBatch} of {created.Count} "
                              + $"in {batch.BatchNumber}.",
            });
        }

        // One notification for the batch, not one per item. Eight separate bells for a single
        // submission is how people learn to ignore the bell.
        await _notifications.RaiseForPermissionAsync(
            Permissions.TaskReview, requesterId,
            $"{batch.BatchNumber}: {items.Count} new request{(items.Count == 1 ? "" : "s")}",
            batch.Title, NotificationService.LinkRequest, null, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Batch {BatchNumber} raised by {UserId} with {Count} items",
            batch.BatchNumber, requesterId, items.Count);

        return await GetAsync(batch.Id, ct);
    }

    public async Task<Result<RequestBatchDetailDto>> GetAsync(long batchId, CancellationToken ct = default)
    {
        var batch = await _db.RequestBatches.AsNoTracking()
            .Where(b => b.Id == batchId)
            .Select(b => new
            {
                b.Id, b.BatchNumber, b.Title, b.Note, b.RequestedByUserId,
                RequestedByDisplayName = b.RequestedByUser.DisplayName,
                b.RequestedAt, b.ClientId,
                ClientName = b.Client != null ? b.Client.Name : null,
            })
            .FirstOrDefaultAsync(ct);

        if (batch is null)
            return Result<RequestBatchDetailDto>.Failure(Error.NotFound("batch.not_found", "Batch not found."));

        var items = await ItemsAsync(batchId, ct);

        var attachments = await _db.Attachments.AsNoTracking()
            .Where(a => a.BatchId == batchId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new AttachmentDto(
                a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes, a.UploadedByUserId, a.CreatedAt))
            .ToListAsync(ct);

        return Result<RequestBatchDetailDto>.Success(new RequestBatchDetailDto(
            batch.Id, batch.BatchNumber, batch.Title, batch.Note,
            batch.RequestedByUserId, batch.RequestedByDisplayName, batch.RequestedAt,
            batch.ClientId, batch.ClientName, items, attachments));
    }

    public Task<PagedResult<RequestBatchSummaryDto>> ReviewQueueAsync(
        PageQuery page, CancellationToken ct = default) =>
        // Oldest first: the review queue is a queue, and the thing that has waited longest is the
        // thing to look at next.
        SummariesAsync(
            _db.RequestBatches.AsNoTracking()
                .Where(b => b.Items.Any(i => AwaitingDecision.Contains(i.Status)))
                .OrderBy(b => b.RequestedAt).ThenBy(b => b.Id),
            page, ct);

    public Task<PagedResult<RequestBatchSummaryDto>> MineAsync(
        long requesterId, PageQuery page, CancellationToken ct = default) =>
        SummariesAsync(
            _db.RequestBatches.AsNoTracking()
                .Where(b => b.RequestedByUserId == requesterId)
                .OrderByDescending(b => b.RequestedAt).ThenByDescending(b => b.Id),
            page, ct);

    public async Task<Result<TriageResult>> ApproveTogetherAsync(
        long batchId, long reviewerId, ApproveTogetherDto decision, CancellationToken ct = default)
    {
        var batch = await _db.RequestBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null)
            return Result<TriageResult>.Failure(Error.NotFound("batch.not_found", "Batch not found."));

        var ids = decision.RequestIds.Distinct().ToList();

        var chosen = await _db.Requests
            .Where(r => ids.Contains(r.Id))
            .OrderBy(r => r.OrdinalInBatch).ThenBy(r => r.Id)
            .ToListAsync(ct);

        if (chosen.Count != ids.Count)
            return Result<TriageResult>.Failure(Error.NotFound(
                "batch.item_not_found", "One of those items no longer exists."));

        // Folding is only meaningful within a batch: the items arrived together and were judged
        // together. Combining unrelated requests from different submissions would make the task's
        // provenance unreadable, and there is no screen that would show it.
        if (chosen.Any(r => r.BatchId != batchId))
            return Result<TriageResult>.Failure(Error.Validation(
                "batch.item_not_in_batch", "Every item must belong to this batch."));

        var alreadyDecided = chosen.FirstOrDefault(r => !AwaitingDecision.Contains(r.Status));
        if (alreadyDecided is not null)
            return Result<TriageResult>.Failure(Error.Conflict(
                "batch.item_already_decided",
                $"{alreadyDecided.RequestNumber} is already \"{StatusLabels.For(alreadyDecided.Status)}\"."));

        // The same rule single-request approval keeps: an unanswered question means the reviewer
        // does not yet have what they asked for.
        var pending = await _db.RequestClarifications
            .Where(c => ids.Contains(c.RequestId) && c.AnsweredAt == null)
            .Select(c => c.RequestId)
            .FirstOrDefaultAsync(ct);

        if (pending != 0)
        {
            var number = chosen.First(r => r.Id == pending).RequestNumber;
            return Result<TriageResult>.Failure(Error.Conflict(
                "request.clarification_pending",
                $"{number} has an unanswered question. Resolve it before approving."));
        }

        var now = _clock.UtcNow;

        // The lowest-ordinal item is the task's primary origin: its words become the task's
        // description, and WorkTask.RequestId points at it. The others are folded in beside it.
        var primary = chosen[0];

        // The highest urgency across the chosen items wins. Averaging them, or taking the first,
        // would let a critical item be quietly downgraded by being submitted next to trivial ones.
        var priority = decision.ApprovedPriority
                       ?? chosen.Min(r => (Priority)(int)r.RequestedUrgency);

        var task = await _taskCreation.CreateFromRequestAsync(
            primary, reviewerId, priority, decision.EstimatedEffortHours, decision.DueDate,
            decision.AcceptanceCriteria, ct);

        if (chosen.Count > 1)
        {
            task.Title = string.IsNullOrWhiteSpace(decision.TaskTitle)
                ? batch.Title
                : decision.TaskTitle.Trim();

            // Every item's words are on the task, numbered. A worker handed three folded requests
            // has to be able to see all three without opening the batch.
            task.Description = string.Join(
                Environment.NewLine + Environment.NewLine,
                chosen.Select((r, i) => $"{i + 1}. {r.Title} ({r.RequestNumber}){Environment.NewLine}{r.Description}"));

            var earliest = chosen.Where(r => r.TargetDate.HasValue).Select(r => r.TargetDate!.Value).ToList();
            if (decision.DueDate is null && earliest.Count > 0)
                task.DueDate = earliest.Min();
        }
        else if (!string.IsNullOrWhiteSpace(decision.TaskTitle))
        {
            task.Title = decision.TaskTitle.Trim();
        }

        foreach (var request in chosen)
        {
            request.Status = RequestStatus.Approved;

            // Several requests, one task. This is the fold, and it needs no join table: the column
            // already means "which task did this become", and it can mean it for all of them.
            request.GeneratedTaskId = task.Id;

            _db.RequestActivities.Add(new RequestActivity
            {
                RequestId = request.Id,
                Type = ActivityType.RequestApproved,
                ActorUserId = reviewerId,
                OccurredAt = now,
                Description = chosen.Count == 1
                    ? $"Approved as {task.TaskNumber}."
                    : $"Approved as {task.TaskNumber}, together with "
                      + $"{string.Join(", ", chosen.Where(r => r.Id != request.Id).Select(r => r.RequestNumber))}.",
            });

            // One audit row per item, not one for the fold: each item was individually approved,
            // and an administrator asking "who approved REQ-000031" must find an answer against
            // that request rather than against a batch operation they have to unpick.
            _audit.Record(
                AuditActions.RequestApproved,
                actorUserId: reviewerId,
                entityType: nameof(Request),
                entityId: request.Id,
                newValues: new
                {
                    TaskId = task.Id,
                    task.TaskNumber,
                    Priority = priority.ToString(),
                    FoldedWith = chosen.Where(r => r.Id != request.Id).Select(r => r.RequestNumber).ToArray(),
                });
        }

        _notifications.RaiseFor(
            new long?[] { primary.RequestedByUserId }, reviewerId,
            chosen.Count == 1
                ? $"Your request {primary.RequestNumber} was approved"
                : $"{chosen.Count} of your requests were approved together",
            $"They are now task {task.TaskNumber}: {task.Title}",
            NotificationService.LinkRequest, primary.Id);

        await _notifications.RaiseForPermissionAsync(
            Permissions.TaskAssign, reviewerId,
            $"{task.TaskNumber} is waiting to be given out",
            task.Title, NotificationService.LinkTask, task.Id, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Reviewer {ReviewerId} approved {Count} item(s) of {BatchNumber} as {TaskNumber}",
            reviewerId, chosen.Count, batch.BatchNumber, task.TaskNumber);

        return Result<TriageResult>.Success(
            new TriageResult(RequestStatus.Approved, task.Id, task.TaskNumber));
    }

    // --- shared ------------------------------------------------------------------------------

    /// <summary>Statuses that still need a reviewer to do something.</summary>
    private static readonly RequestStatus[] AwaitingDecision =
    {
        RequestStatus.Submitted, RequestStatus.InReview, RequestStatus.ClarificationRequired,
    };

    private static readonly RequestStatus[] Declined =
    {
        RequestStatus.Rejected, RequestStatus.Duplicate, RequestStatus.Deferred,
    };

    private async Task<IReadOnlyList<BatchItemSummaryDto>> ItemsAsync(long batchId, CancellationToken ct)
    {
        var rows = await _db.Requests.AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .OrderBy(r => r.OrdinalInBatch).ThenBy(r => r.Id)
            .Select(r => new
            {
                r.Id, r.RequestNumber, r.OrdinalInBatch, r.Title, r.Type, r.RequestedUrgency,
                r.Status, r.GeneratedTaskId,
                GeneratedTaskNumber = r.GeneratedTaskId == null
                    ? null
                    : _db.Tasks.Where(t => t.Id == r.GeneratedTaskId).Select(t => t.TaskNumber).FirstOrDefault(),
            })
            .ToListAsync(ct);

        // Which items share a task is worked out here rather than per row: it is the same question
        // for every row, and asking it once means the fold reads correctly even when a reviewer
        // folded items in two separate sittings.
        return rows.Select(r => new BatchItemSummaryDto(
                r.Id, r.RequestNumber, r.OrdinalInBatch, r.Title, r.Type, r.RequestedUrgency,
                r.Status, StatusLabels.For(r.Status),
                r.GeneratedTaskId, r.GeneratedTaskNumber,
                r.GeneratedTaskId is null
                    ? Array.Empty<string>()
                    : rows.Where(o => o.Id != r.Id && o.GeneratedTaskId == r.GeneratedTaskId)
                        .Select(o => o.RequestNumber)
                        .ToArray()))
            .ToList();
    }

    private async Task<PagedResult<RequestBatchSummaryDto>> SummariesAsync(
        IQueryable<RequestBatch> batches, PageQuery page, CancellationToken ct)
    {
        var total = await batches.CountAsync(ct);

        // The counts are computed in the database rather than by loading every item: a review queue
        // showing twenty-five batches of eight would otherwise pull two hundred rows to display
        // three numbers.
        var items = await batches
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(b => new RequestBatchSummaryDto(
                b.Id, b.BatchNumber, b.Title,
                b.RequestedByUser.DisplayName, b.RequestedAt,
                b.Client != null ? b.Client.Name : null,
                b.Items.Count,
                b.Items.Count(i => AwaitingDecision.Contains(i.Status)),
                b.Items.Count(i => i.Status == RequestStatus.Approved),
                b.Items.Count(i => Declined.Contains(i.Status))))
            .ToListAsync(ct);

        return new PagedResult<RequestBatchSummaryDto>(items, total, page.Page, page.PageSize);
    }
}
