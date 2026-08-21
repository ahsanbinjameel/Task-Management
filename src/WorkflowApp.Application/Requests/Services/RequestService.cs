using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Requests.Services;

public interface IRequestService
{
    Task<Result<RequestDetailDto>> CreateAsync(long requesterId, CreateRequestDto dto, CancellationToken ct = default);
    Task<Result<RequestDetailDto>> UpdateAsync(long requestId, long actingUserId, UpdateRequestDto dto, CancellationToken ct = default);
    Task<Result<RequestDetailDto>> GetAsync(long requestId, CancellationToken ct = default);

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
    public RequestType? Type { get; init; }
    public string? Search { get; init; }
}

/// <summary>
/// Request intake. A request is a submission, not work: nothing here creates a task, sets a
/// priority, or touches a queue. Those only happen at triage, and only on approval.
/// </summary>
public sealed class RequestService : IRequestService
{
    private readonly IWorkflowDbContext _db;
    private readonly INumberGenerator _numbers;
    private readonly IDateTimeProvider _clock;

    public RequestService(IWorkflowDbContext db, INumberGenerator numbers, IDateTimeProvider clock)
    {
        _db = db;
        _numbers = numbers;
        _clock = clock;
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
            ProjectId = dto.ProjectId,
            ClientId = dto.ClientId,
            ModuleId = dto.ModuleId,
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
        await _db.SaveChangesAsync(ct);

        return await GetAsync(request.Id, ct);
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
                $"A request in {request.Status} can no longer be edited."));

        request.Title = dto.Title.Trim();
        request.Description = dto.Description.Trim();
        request.Type = dto.Type;
        request.RequestedUrgency = dto.RequestedUrgency;
        request.BusinessImpact = dto.BusinessImpact;
        request.ExpectedResult = dto.ExpectedResult;
        request.CurrentResult = dto.CurrentResult;
        request.ReproductionSteps = dto.ReproductionSteps;
        request.TargetDate = dto.TargetDate;

        await _db.SaveChangesAsync(ct);
        return await GetAsync(requestId, ct);
    }

    public async Task<Result<RequestDetailDto>> GetAsync(long requestId, CancellationToken ct = default)
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

        var clarifications = request.Clarifications
            .OrderBy(c => c.AskedAt)
            .Select(c => new ClarificationDto(
                c.Id, c.AskedByUserId, c.Question, c.AskedAt, c.AnsweredByUserId, c.Answer, c.AnsweredAt))
            .ToList();

        return Result<RequestDetailDto>.Success(new RequestDetailDto(
            request.Id, request.RequestNumber, request.Title, request.Description, request.Type,
            request.Status, request.RequestedUrgency, request.ProjectId, request.ClientId, request.ModuleId,
            request.BusinessImpact, request.ExpectedResult, request.CurrentResult, request.ReproductionSteps,
            request.RequestedByUserId, requester, request.RequestedAt, request.TargetDate,
            request.RelatedRequestId, request.GeneratedTaskId, clarifications, attachments));
    }

    public async Task<PagedResult<RequestSummaryDto>> ListAsync(
        RequestQuery query, PageQuery page, CancellationToken ct = default)
    {
        var requests = _db.Requests.AsNoTracking();

        if (query.RequestedByUserId is { } requesterId)
            requests = requests.Where(r => r.RequestedByUserId == requesterId);

        if (query.Status is { } status)
            requests = requests.Where(r => r.Status == status);

        if (query.Type is { } type)
            requests = requests.Where(r => r.Type == type);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            requests = requests.Where(r => r.Title.Contains(term) || r.RequestNumber.Contains(term));
        }

        return await ProjectPageAsync(requests.OrderByDescending(r => r.RequestedAt), page, ct);
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

    private async Task<PagedResult<RequestSummaryDto>> ProjectPageAsync(
        IQueryable<Request> query, PageQuery page, CancellationToken ct)
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
                r.Clarifications.Any(c => c.AnsweredAt == null)))
            .ToListAsync(ct);

        return new PagedResult<RequestSummaryDto>(items, page.NormalizedPage, page.NormalizedPageSize, total);
    }
}
