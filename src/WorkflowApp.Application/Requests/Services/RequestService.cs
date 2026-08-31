using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Notifications;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Verifications.Services;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Requests.Services;

public interface IRequestService
{
    Task<Result<RequestDetailDto>> CreateAsync(long requesterId, CreateRequestDto dto, CancellationToken ct = default);
    /// <summary>
    /// A point found in a later round, raised as its own request (PRODUCT-CORE §6). Carries the
    /// shared client and product location; never touches the original or its task.
    /// </summary>
    Task<Result<RequestDetailDto>> CreateFollowUpAsync(
        long originalId, long requesterId, CreateFollowUpDto dto, CancellationToken ct = default);

    Task<Result<RequestDetailDto>> UpdateAsync(long requestId, long actingUserId, UpdateRequestDto dto, CancellationToken ct = default);
    Task<Result<RequestDetailDto>> GetAsync(
        long requestId, StatusAudience audience = StatusAudience.Coordinator,
        CancellationToken ct = default);

    /// <summary>How many requests sit in each status, under the same filters minus status.</summary>
    Task<IReadOnlyList<StatusCountDto>> StatusCountsAsync(RequestQuery query, CancellationToken ct = default);

    /// <summary>What each filterable column can still be narrowed by. See <see cref="FilterOptionsDto"/>.</summary>
    Task<FilterOptionsDto> FilterOptionsAsync(RequestQuery query, CancellationToken ct = default);

    Task<PagedResult<RequestSummaryDto>> ListAsync(
        RequestQuery query, PageQuery page, CancellationToken ct = default);

    /// <summary>The reviewer's work queue: everything awaiting a triage decision.</summary>
    Task<PagedResult<RequestSummaryDto>> ReviewQueueAsync(PageQuery page, CancellationToken ct = default);
}

/// <summary>Filters for listing requests. All optional; null means "no restriction".</summary>
public sealed record RequestQuery
{
    /// <summary>Restricts to one requester — used to enforce <c>Request.ViewOwn</c>.</summary>
    public long? RequestedByUserId { get; init; }

    public RequestStatus? Status { get; init; }

    /// <summary>
    /// The status group being looked at. For a requester that spans the task the request
    /// generated, because after approval the request itself stops moving — see
    /// <see cref="StatusViews"/>. Null or "all" means no status filter.
    /// </summary>
    public string? View { get; init; }

    /// <summary>Who is looking. Decides which tiles exist and what each one covers.</summary>
    public StatusAudience Audience { get; init; } = StatusAudience.Coordinator;

    public RequestType? Type { get; init; }
    public string? Search { get; init; }

    /// <summary>Only requests for this client.</summary>
    public long? ClientId { get; init; }

    /// <summary>Column to order by. Unknown values fall back to newest-first.</summary>
    public string? SortBy { get; init; }

    public bool SortDescending { get; init; } = true;

    /// <summary>
    /// Per-column filters from the grid's filter row. Applied on top of everything above, and — as
    /// with <see cref="Search"/> — deliberately *not* applied when counting the tiles, so a tile
    /// keeps showing how many there would be if you cleared the column you are narrowing by.
    /// </summary>
    public ColumnFilters Columns { get; init; } = ColumnFilters.None;
}

/// <summary>
/// Request intake. A request is a submission, not work: nothing here creates a task, sets a
/// priority, or touches a queue. Those only happen at triage, and only on approval.
/// </summary>
public sealed class RequestService : IRequestService
{
    private readonly IWorkflowDbContext _db;
    private readonly INumberGenerator _numbers;
    private readonly INotificationService _notifications;
    private readonly ILookupService _lookups;
    private readonly IDateTimeProvider _clock;
    private readonly IBusinessCalendar _calendar;
    private readonly IVerificationService _verifications;

    public RequestService(IWorkflowDbContext db, INumberGenerator numbers,
        INotificationService notifications, ILookupService lookups, IDateTimeProvider clock,
        IBusinessCalendar calendar, IVerificationService verifications)
    {
        _db = db;
        _numbers = numbers;
        _notifications = notifications;
        _lookups = lookups;
        _clock = clock;
        _calendar = calendar;
        _verifications = verifications;
    }

    public async Task<Result<RequestDetailDto>> CreateAsync(
        long requesterId, CreateRequestDto dto, CancellationToken ct = default)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == requesterId, ct))
            return Result<RequestDetailDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        var request = new Request
        {
            RequestNumber = await _numbers.NextAsync(NumberSequences.Request, NumberSequences.RequestPrefix, ct),
            Title = dto.Title.Trim(),
            Description = dto.Description.Trim(),
            Type = dto.Type,
            RequestedUrgency = dto.RequestedUrgency,
            ClientId = await _lookups.ResolveClientAsync(dto.ClientName, ct),

            // The product axis, when the requester happened to know. Refined at triage.
            ModuleId = dto.ModuleId,
            FormId = dto.FormId,
            BusinessImpact = dto.BusinessImpact,
            ExpectedResult = dto.ExpectedResult,
            CurrentResult = dto.CurrentResult,
            ReproductionSteps = dto.ReproductionSteps,
            TargetDate = dto.TargetDate,
            RequestedByUserId = requesterId,
            RequestedAt = _clock.UtcNow,
            // Submitted, not approved and not executable. Only triage moves it forward.
            Status = RequestStatus.Submitted
        };

        _db.Requests.Add(request);

        // The review queue is the only thing standing between this and being forgotten.
        await _notifications.RaiseForPermissionAsync(
            Permissions.TaskReview, request.RequestedByUserId,
            $"New request waiting for review: {request.RequestNumber}",
            request.Title, NotificationService.LinkRequest, request.Id, ct);

        await _db.SaveChangesAsync(ct);

        return await GetAsync(request.Id, ct: ct);
    }

    /// <summary>
    /// Raise a point found in a later round of testing (PRODUCT-CORE §6).
    ///
    /// This is the answer to the case the plan calls the Faisal rule: detail-report points on day
    /// one, master-report points on day two. The software answer is neither to punish the requester
    /// for finding things late nor to quietly absorb the new points into work already committed. It
    /// is to make the later round cheap to raise and visible as a later round.
    ///
    /// So it becomes a request of its own — its own number, its own triage decision, its own place
    /// in the queue — carrying the shared client and product location so nobody retypes them. It
    /// deliberately does <b>not</b> touch the original request or whatever task it became. That is
    /// invariant §4.13: once execution starts, committed scope does not silently grow.
    ///
    /// Anyone who can raise a request can raise one of these, including against somebody else's:
    /// finding a second problem while testing a fix is exactly the case this exists for, and the
    /// person who finds it is not always the person who reported the first one.
    /// </summary>
    public async Task<Result<RequestDetailDto>> CreateFollowUpAsync(
        long originalId, long requesterId, CreateFollowUpDto dto, CancellationToken ct = default)
    {
        var original = await _db.Requests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == originalId, ct);

        if (original is null)
            return Result<RequestDetailDto>.Failure(
                Error.NotFound("request.not_found", "Request not found."));

        if (!await _db.Users.AnyAsync(u => u.Id == requesterId, ct))
            return Result<RequestDetailDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        var followUp = new Request
        {
            RequestNumber = await _numbers.NextAsync(
                NumberSequences.Request, NumberSequences.RequestPrefix, ct),
            Title = dto.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description)
                ? dto.Title.Trim()
                : dto.Description.Trim(),
            Type = dto.Type ?? original.Type,
            RequestedUrgency = dto.RequestedUrgency ?? original.RequestedUrgency,

            // Copied, not read through the original. The two are separate requests from here on,
            // and correcting one at triage must not reach back into the other.
            ClientId = original.ClientId,
            ProjectId = original.ProjectId,
            ModuleId = original.ModuleId,
            FormId = original.FormId,
            FormSurfaceId = original.FormSurfaceId,

            RelatedRequestId = original.Id,
            Round = original.Round + 1,

            RequestedByUserId = requesterId,
            RequestedAt = _clock.UtcNow,
            Status = RequestStatus.Submitted,
        };

        _db.Requests.Add(followUp);
        await _db.SaveChangesAsync(ct);

        // The requester's own view: they raised it, and they are the one being handed it back.
        return await GetAsync(followUp.Id, StatusAudience.Requester, ct);
    }

    public async Task<Result<RequestDetailDto>> UpdateAsync(
        long requestId, long actingUserId, UpdateRequestDto dto, CancellationToken ct = default)
    {
        var request = await _db.Requests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
            return Result<RequestDetailDto>.Failure(Error.NotFound("request.not_found", "Request not found."));

        if (request.RequestedByUserId != actingUserId)
            return Result<RequestDetailDto>.Failure(
                Error.Forbidden("request.not_owner", "Only the requester can edit this request."));

        // Once triage has acted on it, editing would invalidate the decision that was made.
        if (request.Status is not (RequestStatus.Submitted or RequestStatus.ClarificationRequired))
            return Result<RequestDetailDto>.Failure(Error.Conflict(
                "request.not_editable",
                $"This request is now \"{StatusLabels.For(request.Status)}\", so it can no longer be "
                + "changed \u2014 the work has been planned around what it says. If you have found "
                + "something else, raise it as a follow-up: it keeps its link to this one and gets "
                + "looked at on its own, without moving the finish line of what is already in hand."));

        // Compare before writing, so the history can say what actually changed rather than just
        // "it was edited". A reviewer who already read this request needs to know which parts to
        // re-read, not that something somewhere moved.
        var changes = new List<string>();

        void Track(string field, string? before, string? after)
        {
            if ((before ?? string.Empty).Trim() != (after ?? string.Empty).Trim())
                changes.Add(field);
        }

        Track("title", request.Title, dto.Title);
        Track("description", request.Description, dto.Description);
        Track("what was expected", request.ExpectedResult, dto.ExpectedResult);
        Track("what happens now", request.CurrentResult, dto.CurrentResult);
        Track("business impact", request.BusinessImpact, dto.BusinessImpact);
        Track("steps to reproduce", request.ReproductionSteps, dto.ReproductionSteps);
        if (request.Type != dto.Type) changes.Add("type");
        if (request.RequestedUrgency != dto.RequestedUrgency) changes.Add("urgency");
        if (request.TargetDate != dto.TargetDate) changes.Add("needed-by date");

        var newClientId = await _lookups.ResolveClientAsync(dto.ClientName, ct);
        if (request.ClientId != newClientId && dto.ClientName is not null) changes.Add("client");

        // Nothing actually changed — do not manufacture history or wake a reviewer for it.
        if (changes.Count == 0) return await GetAsync(requestId, ct: ct);

        request.Title = dto.Title.Trim();
        request.Description = dto.Description.Trim();
        request.Type = dto.Type;
        request.RequestedUrgency = dto.RequestedUrgency;
        request.BusinessImpact = dto.BusinessImpact;
        request.ExpectedResult = dto.ExpectedResult;
        request.CurrentResult = dto.CurrentResult;
        request.ReproductionSteps = dto.ReproductionSteps;
        request.TargetDate = dto.TargetDate;
        if (dto.ClientName is not null) request.ClientId = newClientId;

        var summary = changes.Count == 1
            ? $"Requester updated the {changes[0]}."
            : $"Requester updated the {string.Join(", ", changes.Take(changes.Count - 1))} "
              + $"and {changes[^1]}.";

        _db.RequestActivities.Add(new RequestActivity
        {
            RequestId = request.Id,
            Type = ActivityType.RequestEdited,
            ActorUserId = actingUserId,
            OccurredAt = _clock.UtcNow,
            Description = summary
        });

        // A reviewer may already have read this and formed a view. Silently changing it underneath
        // them is how a decision gets made against text nobody re-read.
        await _notifications.RaiseForPermissionAsync(
            Permissions.TaskReview, actingUserId,
            $"{request.RequestNumber} was changed by the requester",
            summary, NotificationService.LinkRequest, request.Id, ct);

        await _db.SaveChangesAsync(ct);
        return await GetAsync(requestId, ct: ct);
    }

    public async Task<Result<RequestDetailDto>> GetAsync(
        long requestId, StatusAudience audience = StatusAudience.Coordinator,
        CancellationToken ct = default)
    {
        var request = await _db.Requests.AsNoTracking()
            .Include(r => r.Clarifications)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

        if (request is null)
            return Result<RequestDetailDto>.Failure(Error.NotFound("request.not_found", "Request not found."));

        var requester = await _db.Users.AsNoTracking()
            .Where(u => u.Id == request.RequestedByUserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct) ?? "(unknown)";

        var attachments = await _db.Attachments.AsNoTracking()
            .Where(a => a.RequestId == requestId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new AttachmentDto(
                a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes, a.UploadedByUserId, a.CreatedAt))
            .ToListAsync(ct);

        // Newest last, so the story reads downwards like a conversation.
        var activity = await _db.RequestActivities.AsNoTracking()
            .Where(a => a.RequestId == requestId)
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
            .Select(a => new RequestActivityDto(
                a.Id, a.Type.ToString(), a.ActorUserId,
                _db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.DisplayName).FirstOrDefault(),
                a.OccurredAt, a.Description))
            .ToListAsync(ct);

        var clarifications = request.Clarifications
            .OrderBy(c => c.AskedAt)
            .Select(c => new ClarificationDto(
                c.Id, c.AskedByUserId, c.Question, c.AskedAt, c.AnsweredByUserId, c.Answer, c.AnsweredAt))
            .ToList();

        var clientName = request.ClientId is { } clientId
            ? await _db.Clients.AsNoTracking().Where(c => c.Id == clientId)
                .Select(c => c.Name).FirstOrDefaultAsync(ct)
            : null;

        var progress = await ProgressAsync(request.GeneratedTaskId, audience, ct);
        var view = StatusViews.RequestViewOf(audience, request.Status, progress?.TaskStatus);

        // Only looked up when the request actually came in a batch, so the ordinary single-request
        // case costs nothing.
        var batch = request.BatchId is { } batchId
            ? await _db.RequestBatches.AsNoTracking()
                .Where(b => b.Id == batchId)
                .Select(b => new { b.BatchNumber, ItemCount = b.Items.Count })
                .FirstOrDefaultAsync(ct)
            : null;

        // Whatever has been checked on this request, so a reviewer deciding what to do next reads
        // the findings on the screen where the decision is made. Empty for the ordinary request
        // that never needed a check, which is most of them.
        var verifications = await _verifications.ForRequestAsync(requestId, ct);

        // The request this came out of, when it is a later round (PRODUCT-CORE §6). The number
        // rather than the id, because a screen cannot print a link from a number nobody can read.
        // Where in the product this is, joined by the one place that formats it.
        var productLocation = ProductLocation.Format(
            request.ModuleId is { } moduleId
                ? await _db.Modules.AsNoTracking().Where(m => m.Id == moduleId)
                    .Select(m => m.Name).FirstOrDefaultAsync(ct)
                : null,
            request.FormId is { } formId
                ? await _db.Forms.AsNoTracking().Where(f => f.Id == formId)
                    .Select(f => f.Name).FirstOrDefaultAsync(ct)
                : null,
            request.FormSurfaceId is { } surfaceId
                ? await _db.FormSurfaces.AsNoTracking().Where(x => x.Id == surfaceId)
                    .Select(x => x.Name).FirstOrDefaultAsync(ct)
                : null);

        var relatedNumber = request.RelatedRequestId is { } relatedId
            ? await _db.Requests.AsNoTracking().Where(r => r.Id == relatedId)
                .Select(r => r.RequestNumber).FirstOrDefaultAsync(ct)
            : null;

        return Result<RequestDetailDto>.Success(new RequestDetailDto(
            request.Id, request.RequestNumber, request.Title, request.Description, request.Type,
            request.Status, request.RequestedUrgency, request.ClientId, clientName,
            request.BusinessImpact, request.ExpectedResult, request.CurrentResult, request.ReproductionSteps,
            request.RequestedByUserId, requester, request.RequestedAt, request.TargetDate,
            productLocation,
            request.RelatedRequestId, relatedNumber, request.Round,
            request.GeneratedTaskId, activity, clarifications, attachments,
            verifications,
            view.Key, view.Label, progress,
            request.BatchId, batch?.BatchNumber, request.OrdinalInBatch, batch?.ItemCount ?? 0));
    }

    public async Task<PagedResult<RequestSummaryDto>> ListAsync(
        RequestQuery query, PageQuery page, CancellationToken ct = default)
    {
        var requests = _db.Requests.AsNoTracking();

        if (query.RequestedByUserId is { } requesterId)
            requests = requests.Where(r => r.RequestedByUserId == requesterId);

        // The filter row is applied *here and only here*, never in StatusCountsAsync. The tiles are
        // the navigation: a count that fell towards zero while someone typed into a column would be
        // a number nobody could aim at. Structural rather than a comment on the counting method,
        // because the two have already drifted apart once.
        requests = ApplyColumnFilters(ApplyFilters(requests, query, includeStatus: true), query.Columns);

        return await ProjectPageAsync(Sort(requests, query), page, ct, query.Audience);
    }

    public Task<PagedResult<RequestSummaryDto>> ReviewQueueAsync(PageQuery page, CancellationToken ct = default)
    {
        // Everything a reviewer still owns. Clarification-required sits with the requester, so it
        // is excluded — it would otherwise clog the queue with items nobody can act on.
        var queue = _db.Requests.AsNoTracking()
            .Where(r => r.Status == RequestStatus.Submitted || r.Status == RequestStatus.InReview)
            // Most urgent first, then oldest first, so nothing starves at the bottom.
            .OrderBy(r => r.RequestedUrgency)
            .ThenBy(r => r.RequestedAt);

        return ProjectPageAsync(queue, page, ct);
    }

    /// <summary>Ordering, driven from the column header. Applied in the database, not on the page.</summary>
    private static IQueryable<Request> Sort(IQueryable<Request> requests, RequestQuery query)
    {
        var descending = query.SortDescending;

        return query.SortBy?.ToLowerInvariant() switch
        {
            "number" => descending ? requests.OrderByDescending(r => r.RequestNumber) : requests.OrderBy(r => r.RequestNumber),
            "title" => descending ? requests.OrderByDescending(r => r.Title) : requests.OrderBy(r => r.Title),
            "status" => descending ? requests.OrderByDescending(r => r.Status) : requests.OrderBy(r => r.Status),
            "urgency" => descending ? requests.OrderByDescending(r => r.RequestedUrgency) : requests.OrderBy(r => r.RequestedUrgency),
            "requester" => descending
                ? requests.OrderByDescending(r => r.RequestedByUser.DisplayName)
                : requests.OrderBy(r => r.RequestedByUser.DisplayName),
            "client" => descending
                ? requests.OrderByDescending(r => r.ClientId == null).ThenByDescending(r => r.ClientId)
                : requests.OrderBy(r => r.ClientId == null).ThenBy(r => r.ClientId),
            _ => descending ? requests.OrderByDescending(r => r.RequestedAt) : requests.OrderBy(r => r.RequestedAt),
        };
    }

    /// <summary>
    /// The filters, in one place, so the tiles and the list can never disagree. `includeStatus` is
    /// false when counting: a tile showing "Approved 4" has to be counted across everything the
    /// other filters allow, not within the status already selected.
    /// </summary>
    private IQueryable<Request> ApplyFilters(
        IQueryable<Request> requests, RequestQuery query, bool includeStatus)
    {
        if (includeStatus && query.Status is { } status)
            requests = requests.Where(r => r.Status == status);

        if (includeStatus && StatusViews.FindRequestView(query.Audience, query.View) is { } view)
        {
            // Two halves of one question: a request with no task yet answers for itself, and one
            // that has a task answers with the task's status. Unconditional — every audience's
            // table now carries the journey past approval, so there is no audience for which
            // folding would judge a request against an empty task list and silently drop it.
            var requestStatuses = view.RequestStatuses.ToList();
            var taskStatuses = view.TaskStatuses.ToList();

            requests = requests.Where(r =>
                (r.GeneratedTaskId == null && requestStatuses.Contains(r.Status))
                || (r.GeneratedTaskId != null && _db.Tasks
                        .Where(t => t.Id == r.GeneratedTaskId)
                        .Any(t => taskStatuses.Contains(t.Status))));
        }

        if (query.Type is { } type)
            requests = requests.Where(r => r.Type == type);

        if (query.ClientId is { } clientId)
            requests = requests.Where(r => r.ClientId == clientId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            requests = requests.Where(r => r.Title.Contains(term) || r.RequestNumber.Contains(term));
        }

        return requests;
    }

    /// <summary>
    /// The grid's filter row. Keys match the column names the client renders, so the row is
    /// generated from the columns rather than hand-listed on both sides.
    ///
    /// "Requester" is the one that replaced a toggle: filtering the column by a person is what
    /// "only mine" used to do, and one control that answers "whose?" beats a switch that answers it
    /// only for you.
    /// </summary>
    private IQueryable<Request> ApplyColumnFilters(IQueryable<Request> requests, ColumnFilters columns)
    {
        if (!columns.Any) return requests;

        if (columns.Text("number") is { } number)
            requests = requests.Where(r => r.RequestNumber.Contains(number));

        if (columns.Text("title") is { } title)
            requests = requests.Where(r => r.Title.Contains(title));

        // Several values per column read as "any of these" — the question a filter row is asked
        // ("show me Critical *and* High") is an OR within the column and an AND across columns.
        var clientIds = columns.Ids("client");
        if (clientIds.Count > 0)
            requests = requests.Where(r => r.ClientId != null && clientIds.Contains(r.ClientId.Value));

        var types = columns.Enums<RequestType>("type");
        if (types.Count > 0)
            requests = requests.Where(r => types.Contains(r.Type));

        var urgencies = columns.Enums<RequestedUrgency>("urgency");
        if (urgencies.Count > 0)
            requests = requests.Where(r => urgencies.Contains(r.RequestedUrgency));

        // By name rather than by id: the person list needed for a dropdown is behind Task.Assign,
        // which a reviewer need not have, and a filter that 403s for half its users is worse than
        // one that matches on what the column already shows.
        if (columns.Text("requester") is { } requester)
        {
            requests = requests.Where(r => _db.Users
                .Any(u => u.Id == r.RequestedByUserId
                    && (u.DisplayName.Contains(requester) || u.UserName.Contains(requester))));
        }

        // Everything raised on that business day — see the note on the task grid's due-date filter
        // for why this must not be UTC midnight.
        if (columns.Date("raised") is { } raised)
        {
            var (from, to) = _calendar.DayRange(raised);
            requests = requests.Where(r => r.RequestedAt >= from && r.RequestedAt < to);
        }

        // The person the generated task sits with — the column a requester actually scans.
        if (columns.Text("responsible") is { } responsible)
        {
            requests = requests.Where(r => r.GeneratedTaskId != null && _db.Tasks
                .Any(t => t.Id == r.GeneratedTaskId
                    && t.PrimaryAssigneeUser != null
                    && (t.PrimaryAssigneeUser.DisplayName.Contains(responsible)
                        || t.PrimaryAssigneeUser.UserName.Contains(responsible))));
        }

        return requests;
    }

    /// <summary>
    /// What each column's dropdown should still offer, given the other columns. Each is computed
    /// with its own filter removed — see <see cref="FilterOptionsDto"/>.
    /// </summary>
    public async Task<FilterOptionsDto> FilterOptionsAsync(
        RequestQuery query, CancellationToken ct = default)
    {
        var columns = new Dictionary<string, IReadOnlyList<string>>();

        var requests = _db.Requests.AsNoTracking();

        if (query.RequestedByUserId is { } requesterId)
            requests = requests.Where(r => r.RequestedByUserId == requesterId);

        var basis = ApplyFilters(requests, query, includeStatus: true);

        IQueryable<Request> Excluding(string key) =>
            ApplyColumnFilters(basis, query.Columns.Without(key));

        columns["client"] = (await Excluding("client")
                .Where(r => r.ClientId != null)
                .Select(r => r.ClientId!.Value)
                .Distinct()
                .ToListAsync(ct))
            .Select(id => id.ToString())
            .ToList();

        columns["urgency"] = (await Excluding("urgency")
                .Select(r => r.RequestedUrgency)
                .Distinct()
                .ToListAsync(ct))
            .Select(u => u.ToString())
            .ToList();

        columns["type"] = (await Excluding("type")
                .Select(r => r.Type)
                .Distinct()
                .ToListAsync(ct))
            .Select(t => t.ToString())
            .ToList();

        return new FilterOptionsDto(columns);
    }

    public async Task<IReadOnlyList<StatusCountDto>> StatusCountsAsync(
        RequestQuery query, CancellationToken ct = default)
    {
        var requests = _db.Requests.AsNoTracking();

        if (query.RequestedByUserId is { } requesterId)
            requests = requests.Where(r => r.RequestedByUserId == requesterId);

        var filtered = ApplyFilters(requests, query, includeStatus: false);

        // Requests nobody has approved yet answer for themselves...
        var byRequestStatus = await filtered
            .Where(r => r.GeneratedTaskId == null)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // ...and the approved ones answer with their task. The two halves must split the set on
        // exactly the condition ApplyFilters splits it on, or a tile counts what the list will not
        // show — which is how the Approved tile once read two above a list of none.
        var byTaskStatus = await (from r in filtered
                                  where r.GeneratedTaskId != null
                                  join t in _db.Tasks on r.GeneratedTaskId equals t.Id
                                  group t by t.Status into g
                                  select new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var requestCounts = byRequestStatus.ToDictionary(c => c.Status, c => c.Count);
        var taskCounts = byTaskStatus.ToDictionary(c => c.Status, c => c.Count);

        // Every view is returned, including the empty ones: a tile that vanishes when it reaches
        // zero makes the row jump about, and "none waiting for review" is information.
        return StatusViews.ForRequests(query.Audience)
            .Select(view => new StatusCountDto(
                view.Key,
                view.Label,
                view.RequestStatuses.Sum(s => requestCounts.TryGetValue(s, out var n) ? n : 0)
                    + view.TaskStatuses.Sum(s => taskCounts.TryGetValue(s, out var n) ? n : 0)))
            .ToList();
    }

    /// <summary>
    /// Reads the generated task back onto the request in the words the reader uses.
    ///
    /// Deliberately a summary and not a copy of the task: enough to answer "what is happening?"
    /// without becoming a second, staler task screen that has to be kept in step.
    /// </summary>
    private async Task<RequestProgressDto?> ProgressAsync(
        long? taskId, StatusAudience audience, CancellationToken ct)
    {
        if (taskId is not { } id) return null;

        var task = await _db.Tasks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new
            {
                t.Id,
                t.TaskNumber,
                t.Status,
                t.ProgressPercent,
                t.DueDate,
                Responsible = t.PrimaryAssigneeUser == null ? null : t.PrimaryAssigneeUser.DisplayName,
            })
            .FirstOrDefaultAsync(ct);

        if (task is null) return null;

        var support = await _db.TaskCollaborators.AsNoTracking()
            .Where(c => c.TaskId == id)
            .Select(c => c.User.DisplayName)
            .ToListAsync(ct);

        var sessions = await _db.WorkSessions.AsNoTracking()
            .Where(s => s.TaskId == id)
            .Select(s => new { s.SessionStart, s.SessionEnd })
            .ToListAsync(ct);

        var worked = sessions
            .Where(s => s.SessionEnd.HasValue)
            .Aggregate(TimeSpan.Zero, (sum, s) => sum + (s.SessionEnd!.Value - s.SessionStart));

        // The most recent note anyone deliberately shared with the requester. Internal notes are
        // excluded at the source rather than filtered in the UI — the same rule the comment
        // thread already applies, applied once more here.
        var update = await _db.TaskComments.AsNoTracking()
            .Where(c => c.TaskId == id && c.VisibleToRequester)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Select(c => new
            {
                c.Body,
                c.CreatedAt,
                Author = _db.Users.Where(u => u.Id == c.AuthorUserId)
                    .Select(u => u.DisplayName).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);

        var lastCheck = await _db.QCReviews.AsNoTracking()
            .Where(q => q.TaskId == id)
            .OrderByDescending(q => q.AttemptNumber)
            .Select(q => new { q.Result, q.ReviewedAt })
            .FirstOrDefaultAsync(ct);

        var quality = task.Status switch
        {
            WorkTaskStatus.CompletedReadyForQC => "Waiting to be checked",
            WorkTaskStatus.QCReview => "Being checked now",
            WorkTaskStatus.QCFailedRework => "Checked, and sent back for more work",
            WorkTaskStatus.QCPassed or WorkTaskStatus.ReadyForClosure => "Passed",
            WorkTaskStatus.Closed => lastCheck is null ? "Not needed" : "Passed",
            _ => lastCheck is null ? "Not started yet" : "Last check: " + lastCheck.Result,
        };

        // Why it is stopped, in the words whoever stopped it used. Pausing and blocking both had
        // to give a reason, so there is one to show.
        var waiting = task.Status is WorkTaskStatus.Paused or WorkTaskStatus.Blocked
                or WorkTaskStatus.OnHold or WorkTaskStatus.Deferred
            ? await _db.StatusHistories.AsNoTracking()
                .Where(h => h.TaskId == id && h.ToStatus == task.Status)
                .OrderByDescending(h => h.ChangedAt)
                .Select(h => h.Reason)
                .FirstOrDefaultAsync(ct)
            : null;

        var view = StatusViews.ViewOf(audience, task.Status);

        return new RequestProgressDto(
            task.Id, task.TaskNumber, task.Status,
            view?.Key ?? task.Status.ToString().ToLowerInvariant(),
            view?.Label ?? StatusLabels.For(task.Status),
            task.Responsible,
            support,
            task.ProgressPercent,
            worked,
            sessions.Count == 0 ? null : sessions.Min(s => s.SessionStart),
            task.DueDate,
            update?.Body,
            update?.Author,
            update?.CreatedAt,
            quality,
            waiting);
    }

    private async Task<PagedResult<RequestSummaryDto>> ProjectPageAsync(
        IQueryable<Request> query, PageQuery page, CancellationToken ct,
        StatusAudience audience = StatusAudience.Coordinator)
    {
        var total = await query.CountAsync(ct);

        var items = await query
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .Select(r => new RequestSummaryDto(
                r.Id,
                r.RequestNumber,
                r.Title,
                r.Type,
                r.Status,
                r.RequestedUrgency,
                r.RequestedByUserId,
                r.RequestedByUser.DisplayName,
                r.RequestedAt,
                r.TargetDate,
                r.GeneratedTaskId,
                r.Attachments.Count,
                r.Clarifications.Any(c => c.AnsweredAt == null),
                // Passed explicitly rather than relying on the record's defaults: EF cannot
                // translate a constructor with optional arguments inside a projection.
                r.ClientId,
                r.ClientId == null
                    ? null
                    : _db.Clients.Where(c => c.Id == r.ClientId).Select(c => c.Name).FirstOrDefault(),
                // The generated task, folded onto the request. This is what spares the requester
                // a second screen: who has it, how far along it is, and when it last moved.
                r.GeneratedTaskId == null
                    ? null
                    : _db.Tasks.Where(t => t.Id == r.GeneratedTaskId)
                        .Select(t => (WorkTaskStatus?)t.Status).FirstOrDefault(),
                "",
                "",
                r.GeneratedTaskId == null
                    ? null
                    : _db.Tasks.Where(t => t.Id == r.GeneratedTaskId)
                        .Select(t => t.PrimaryAssigneeUser!.DisplayName).FirstOrDefault(),
                r.GeneratedTaskId == null
                    ? 0
                    : _db.Tasks.Where(t => t.Id == r.GeneratedTaskId)
                        .Select(t => t.ProgressPercent).FirstOrDefault(),
                r.GeneratedTaskId == null
                    ? r.UpdatedAt ?? r.RequestedAt
                    : _db.Tasks.Where(t => t.Id == r.GeneratedTaskId)
                        .Select(t => t.UpdatedAt ?? t.CreatedAt).FirstOrDefault()))
            .ToListAsync(ct);

        // The label is decided in one place, in C#, so it cannot be translated into SQL. Applied
        // after the query rather than duplicated as a giant CASE expression.
        var labelled = items
            .Select(r =>
            {
                var view = StatusViews.RequestViewOf(audience, r.Status, r.TaskStatus);
                return r with { ViewKey = view.Key, ViewLabel = view.Label };
            })
            .ToList();

        return new PagedResult<RequestSummaryDto>(
            labelled, page.NormalizedPage, page.NormalizedPageSize, total);
    }
}
