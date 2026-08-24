using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Requests.Services;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// Several things asked for at once.
///
/// A batch is an intake convenience, not a second workflow: every item is an ordinary request with
/// its own number and its own triage decision, so the existing endpoints — triage, clarifications,
/// editing before approval — all work on a batch item without knowing it is one. Only the two
/// genuinely new operations live here: creating items together, and folding several approved items
/// into one task.
///
/// Nothing here creates work. Approval does, and approval still goes through
/// <c>TaskCreationService</c>.
/// </summary>
[Route("api/requests/batches")]
public sealed class RequestBatchesController : ApiControllerBase
{
    private readonly IRequestBatchService _batches;
    private readonly IAttachmentService _attachments;

    public RequestBatchesController(IRequestBatchService batches, IAttachmentService attachments)
    {
        _batches = batches;
        _attachments = attachments;
    }

    /// <summary>Raise several requests at once, sharing a client, a note and a set of files.</summary>
    [HttpPost]
    [HasPermission(Permissions.RequestCreate)]
    [ProducesResponseType(typeof(RequestBatchDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateRequestBatchDto dto, CancellationToken ct)
    {
        var result = await _batches.CreateAsync(CurrentUserId, dto, ct);
        if (result.IsFailure) return Problem(result.Error!);

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// The batch and its items. Readable by the person who raised it and by anyone who reviews,
    /// coordinates or reports — the same audiences that can already see the items individually,
    /// so the wrapper cannot reveal anything its contents do not.
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(RequestBatchDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var result = await _batches.GetAsync(id, ct);
        if (result.IsFailure) return Problem(result.Error!);

        if (result.Value!.RequestedByUserId != CurrentUserId && !CanSeeEveryRequest)
        {
            // Not Found rather than Forbidden, the same as the task detail: "you may not see this"
            // still confirms it exists.
            return Problem(Error.NotFound("batch.not_found", "Batch not found."));
        }

        return Ok(result.Value);
    }

    /// <summary>The caller's own batches, newest first.</summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(PagedResult<RequestBatchSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] PageQuery page, CancellationToken ct)
        => Ok(await _batches.MineAsync(CurrentUserId, page, ct));

    /// <summary>Batches with at least one item still awaiting a decision, oldest first.</summary>
    [HttpGet("review-queue")]
    [HasPermission(Permissions.TaskReview)]
    [ProducesResponseType(typeof(PagedResult<RequestBatchSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReviewQueue([FromQuery] PageQuery page, CancellationToken ct)
        => Ok(await _batches.ReviewQueueAsync(page, ct));

    /// <summary>
    /// Approve several items as one piece of work. Gated on <c>Task.Approve</c> like every other
    /// route into task creation — folding is a shortcut through the paperwork, not through the
    /// permission.
    /// </summary>
    [HttpPost("{id:long}/approve-together")]
    [HasPermission(Permissions.TaskApprove)]
    [ProducesResponseType(typeof(TriageResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveTogether(
        long id, [FromBody] ApproveTogetherDto decision, CancellationToken ct)
        => FromResult(await _batches.ApproveTogetherAsync(id, CurrentUserId, decision, ct));

    /// <summary>A file that belongs to the whole submission rather than to one item.</summary>
    [HttpPost("{id:long}/attachments")]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Upload(long id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Problem(Error.Validation("attachment.empty", "No file was supplied."));

        await using var stream = file.OpenReadStream();

        return FromResult(await _attachments.UploadAsync(
            requestId: null, taskId: null, uploaderId: CurrentUserId,
            stream, file.FileName, file.ContentType, ct, batchId: id));
    }

    /// <summary>The audiences that can already list other people's requests.</summary>
    private bool CanSeeEveryRequest =>
        HasPermission(Permissions.RequestViewAll)
        || HasPermission(Permissions.TaskReview)
        || HasPermission(Permissions.TaskAssign)
        || HasPermission(Permissions.ReportsView);
}
