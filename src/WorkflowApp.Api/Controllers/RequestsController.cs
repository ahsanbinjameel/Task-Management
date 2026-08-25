using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Requests.Services;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// Request intake and triage. Nothing on this controller creates work — approval does, and
/// approval lives behind <c>Task.Approve</c>.
/// </summary>
public sealed class RequestsController : ApiControllerBase
{
    private readonly IRequestService _requests;
    private readonly IRequestTriageService _triage;
    private readonly IAttachmentService _attachments;

    public RequestsController(
        IRequestService requests, IRequestTriageService triage, IAttachmentService attachments)
    {
        _requests = requests;
        _triage = triage;
        _attachments = attachments;
    }

    [HttpPost]
    [HasPermission(Permissions.RequestCreate)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateRequestDto dto, CancellationToken ct)
    {
        var result = await _requests.CreateAsync(CurrentUserId, dto, ct);
        if (result.IsFailure) return Problem(result.Error!);

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// Lists requests. Callers with only <c>Request.ViewOwn</c> are silently scoped to their own —
    /// the filter is applied server-side, never trusted from the query string.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<RequestSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] RequestStatus? status,
        [FromQuery] string? view,
        [FromQuery] RequestType? type,
        [FromQuery] string? search,
        [FromQuery] long? clientId,
        [FromQuery] bool mine = false,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        // The grid's filter row, as col[number]=REQ-1&col[title]=invoice. A dictionary because the
        // row is generated from the columns — see ColumnFilters.
        [FromQuery(Name = "col")] Dictionary<string, string?>? col = null,
        CancellationToken ct = default)
    {
        var canViewAll = HasPermission(Permissions.RequestViewAll);

        var query = new RequestQuery
        {
            Status = status,
            View = view,
            Audience = Audience,
            Type = type,
            Search = search,
            ClientId = clientId,
            SortBy = sortBy,
            SortDescending = sortDescending,
            RequestedByUserId = (!canViewAll || mine) ? CurrentUserId : null,
            Columns = new ColumnFilters(col)
        };

        return Ok(await _requests.ListAsync(query, new PageQuery { Page = page, PageSize = pageSize }, ct));
    }

    /// <summary>Counts for the status tiles, scoped exactly like the list below them.</summary>
    [HttpGet("status-counts")]
    [ProducesResponseType(typeof(IReadOnlyList<StatusCountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> StatusCounts(
        [FromQuery] RequestType? type,
        [FromQuery] long? clientId,
        [FromQuery] string? search,
        [FromQuery] bool mine = false,
        CancellationToken ct = default)
    {
        var canViewAll = HasPermission(Permissions.RequestViewAll);

        return Ok(await _requests.StatusCountsAsync(new RequestQuery
        {
            RequestedByUserId = (!canViewAll || mine) ? CurrentUserId : null,
            Audience = Audience,
            Type = type,
            ClientId = clientId,
            Search = search,
        }, ct));
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var result = await _requests.GetAsync(id, Audience, ct);
        if (result.IsFailure) return Problem(result.Error!);

        // Without ViewAll, a requester may only open their own submissions.
        if (!HasPermission(Permissions.RequestViewAll) && result.Value!.RequestedByUserId != CurrentUserId)
            return Problem(Error.Forbidden("request.forbidden", "You cannot view this request."));

        return Ok(result.Value);
    }

    [HttpPut("{id:long}")]
    [HasPermission(Permissions.RequestCreate)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateRequestDto dto, CancellationToken ct)
        => FromResult(await _requests.UpdateAsync(id, CurrentUserId, dto, ct));

    /// <summary>The reviewer's queue: everything still awaiting a decision.</summary>
    [HttpGet("review-queue")]
    [HasPermission(Permissions.TaskReview)]
    [ProducesResponseType(typeof(PagedResult<RequestSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReviewQueue(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await _requests.ReviewQueueAsync(new PageQuery { Page = page, PageSize = pageSize }, ct));

    [HttpPost("{id:long}/start-review")]
    [HasPermission(Permissions.TaskReview)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartReview(long id, CancellationToken ct)
        => FromResult(await _triage.StartReviewAsync(id, CurrentUserId, ct));

    /// <summary>
    /// Records the triage decision. Approving requires <c>Task.Approve</c> on top of
    /// <c>Task.Review</c>, because approval is what commits the organisation to the work.
    /// </summary>
    [HttpPost("{id:long}/triage")]
    [HasPermission(Permissions.TaskReview)]
    [ProducesResponseType(typeof(TriageResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Triage(long id, [FromBody] TriageDecisionDto decision, CancellationToken ct)
    {
        if (decision.Outcome == TriageOutcome.Approve && !HasPermission(Permissions.TaskApprove))
            return Problem(Error.Forbidden("triage.approve_forbidden", "Approving requires Task.Approve."));

        return FromResult(await _triage.DecideAsync(id, CurrentUserId, decision, ct));
    }

    /// <summary>The requester answers an open clarification, returning the request to review.</summary>
    [HttpPost("clarifications/{clarificationId:long}/answer")]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnswerClarification(
        long clarificationId, [FromBody] AnswerClarificationDto dto, CancellationToken ct)
        => FromResult(await _triage.AnswerClarificationAsync(clarificationId, CurrentUserId, dto.Answer, ct));

    [HttpPost("{id:long}/attachments")]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Upload(long id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Problem(Error.Validation("attachment.empty", "No file was supplied."));

        await using var stream = file.OpenReadStream();

        return FromResult(await _attachments.UploadAsync(
            requestId: id, taskId: null, uploaderId: CurrentUserId,
            stream, file.FileName, file.ContentType, ct));
    }
}

/// <summary>Attachment download and removal, shared by requests and tasks.</summary>
[Route("api/attachments")]
public sealed class AttachmentsController : ApiControllerBase
{
    private readonly IAttachmentService _attachments;

    public AttachmentsController(IAttachmentService attachments) => _attachments = attachments;

    /// <summary>
    /// Streams a stored file. Access goes through this endpoint rather than a static path so it can
    /// be authorized and recorded.
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var result = await _attachments.DownloadAsync(id, CurrentUserId, ct);
        if (result.IsFailure) return Problem(result.Error!);

        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => FromResult(await _attachments.DeleteAsync(id, CurrentUserId, ct));
}
