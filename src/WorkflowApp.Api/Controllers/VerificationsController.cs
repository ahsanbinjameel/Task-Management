using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Requests.Services;
using WorkflowApp.Application.Verifications.Dtos;
using WorkflowApp.Application.Verifications.Services;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// Assigned investigation: "go and find out whether this is really broken".
///
/// <para>
/// The permission split here is the point. <c>Verification.Create</c> raises and routes;
/// <c>Verification.Work</c> investigates and reports. Neither implies <c>Task.Work</c> and neither
/// implies <c>Workforce.TrackShift</c> — a checker whose hours are measured and one whose hours are
/// not are both legitimate configurations, and which of the two an organisation wants is decided in
/// the role editor rather than here.
/// </para>
///
/// <para>
/// Two rules are enforced in the service rather than by an attribute, because the answer depends on
/// the record and not only on the caller: only the assigned checker may start one or report on it,
/// and only they may attach evidence to it.
/// </para>
/// </summary>
[Route("api/verifications")]
public sealed class VerificationsController : ApiControllerBase
{
    private readonly IVerificationService _verifications;
    private readonly IAttachmentService _attachments;

    public VerificationsController(
        IVerificationService verifications, IAttachmentService attachments)
    {
        _verifications = verifications;
        _attachments = attachments;
    }

    /// <summary>
    /// Everything the caller may see. Scoped in the service: without
    /// <see cref="Permissions.VerificationViewAll"/> that is what they raised and what they hold.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<VerificationSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] VerificationStatus? status,
        [FromQuery] bool mineOnly,
        [FromQuery] PageQuery page,
        CancellationToken ct)
        => Ok(await _verifications.ListAsync(CurrentUserId, CurrentPermissions, status, mineOnly, page, ct));

    /// <summary>What is on this checker's desk: assigned or in progress, most urgent first.</summary>
    [HttpGet("my-queue")]
    [HasPermission(Permissions.VerificationWork)]
    [ProducesResponseType(typeof(IReadOnlyList<VerificationSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MyQueue(CancellationToken ct)
        => Ok(await _verifications.MyQueueAsync(CurrentUserId, ct));

    /// <summary>Who a verification can be given to — anyone holding <c>Verification.Work</c>.</summary>
    [HttpGet("assignable-checkers")]
    [HasPermission(Permissions.VerificationCreate)]
    [ProducesResponseType(typeof(IReadOnlyList<AssignableCheckerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignableCheckers(CancellationToken ct)
        => Ok(await _verifications.AssignableCheckersAsync(ct));

    /// <summary>
    /// One verification in full. Returns <b>404</b> rather than 403 when it is out of scope, so a
    /// refusal does not confirm that the number exists.
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(VerificationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
        => FromResult(await _verifications.GetAsync(id, CurrentUserId, CurrentPermissions, ct));

    /// <summary>
    /// Raise a check on something. Needs no request and no task — an independent verification is
    /// the ordinary case, not a degenerate one.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.VerificationCreate)]
    [ProducesResponseType(typeof(VerificationDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateVerificationDto request, CancellationToken ct)
        => FromResult(await _verifications.CreateAsync(CurrentUserId, request, ct));

    /// <summary>Give it to a checker, or move it to a different one. Moving it needs a reason.</summary>
    [HttpPut("{id:long}/assignee")]
    [HasPermission(Permissions.VerificationCreate)]
    [ProducesResponseType(typeof(VerificationDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Assign(
        long id, [FromBody] AssignVerificationDto request, CancellationToken ct)
        => FromResult(await _verifications.AssignAsync(id, CurrentUserId, request, ct));

    /// <summary>
    /// Take an unclaimed check for yourself.
    ///
    /// Gated on <c>Verification.Work</c> rather than <c>Verification.Create</c>: this is a checker
    /// picking up work nobody holds, not a coordinator handing it out. The service refuses anything
    /// somebody already has — moving that is a decision about two people's workloads, and it goes
    /// through the assignee endpoint, which asks why.
    /// </summary>
    [HttpPost("{id:long}/claim")]
    [HasPermission(Permissions.VerificationWork)]
    [ProducesResponseType(typeof(VerificationDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Claim(long id, CancellationToken ct)
        => FromResult(await _verifications.ClaimAsync(id, CurrentUserId, ct));

    /// <summary>Begin looking. Only the assigned checker may, which the service enforces.</summary>
    [HttpPost("{id:long}/start")]
    [HasPermission(Permissions.VerificationWork)]
    [ProducesResponseType(typeof(VerificationDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Start(long id, CancellationToken ct)
        => FromResult(await _verifications.StartAsync(id, CurrentUserId, ct));

    /// <summary>
    /// Record what was found.
    ///
    /// Note what this deliberately does not do: even <c>IssueConfirmed</c> creates no task. The
    /// request goes back to review with the findings attached and a reviewer approves it — or does
    /// not — explicitly, so <c>TaskCreationService</c> keeps its monopoly on creating work.
    /// </summary>
    [HttpPost("{id:long}/result")]
    [HasPermission(Permissions.VerificationWork)]
    [ProducesResponseType(typeof(VerificationDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordResult(
        long id, [FromBody] RecordVerificationResultDto request, CancellationToken ct)
        => FromResult(await _verifications.RecordResultAsync(id, CurrentUserId, request, ct));

    /// <summary>Call it off. Kept with its reason rather than deleted.</summary>
    [HttpPost("{id:long}/cancel")]
    [HasPermission(Permissions.VerificationCreate)]
    [ProducesResponseType(typeof(VerificationDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(
        long id, [FromBody] CancelVerificationDto request, CancellationToken ct)
        => FromResult(await _verifications.CancelAsync(id, CurrentUserId, request, ct));

    /// <summary>
    /// Attach evidence — the screenshot of the wrong figure, the log extract.
    ///
    /// Defaults to <see cref="AttachmentKind.VerificationEvidence"/>, and the service refuses it
    /// from anyone but the assigned checker: evidence for an investigation is the investigator's to
    /// supply, and that is a fact about the record rather than about the caller's permissions.
    /// </summary>
    [HttpPost("{id:long}/attachments")]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Upload(long id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file was supplied." });

        await using var stream = file.OpenReadStream();

        return FromResult(await _attachments.UploadAsync(
            requestId: null, taskId: null, uploaderId: CurrentUserId,
            content: stream, fileName: file.FileName, contentType: file.ContentType, ct: ct,
            kind: AttachmentKind.VerificationEvidence, verificationId: id));
    }
}
