using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Application.Tasks.Services;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// The five-minute job that arrived by phone.
///
/// Every route here acts on the caller's own record and takes no user parameter — quick work is a
/// personal account of somebody's own time, and there is nothing here for a supervisor to edit.
/// Reading somebody else's is done through the daily report, which is already gated on
/// <c>Reports.View</c>.
///
/// Gated on <see cref="Permissions.WorkforceTrackShift"/> rather than <c>Task.Work</c>: this is a
/// time record, and that permission is precisely "this person's hours are measured". Someone whose
/// time is not tracked has nowhere for the record to land.
/// </summary>
[Route("api/quick-work")]
public sealed class QuickWorkController : ApiControllerBase
{
    private readonly IQuickWorkService _quickWork;

    public QuickWorkController(IQuickWorkService quickWork) => _quickWork = quickWork;

    /// <summary>What the caller is recording right now, if anything.</summary>
    [HttpGet("active")]
    [ProducesResponseType(typeof(QuickWorkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        var active = await _quickWork.ActiveAsync(CurrentUserId, ct);
        return active is null ? NoContent() : Ok(active);
    }

    /// <summary>The caller's quick work for a business day. Defaults to today.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<QuickWorkDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForDay([FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _quickWork.ForDayAsync(CurrentUserId, date ?? Today, ct));

    /// <summary>
    /// Start the clock. Pauses whatever task was running, in the same commit, so the
    /// one-thing-at-a-time rule is never briefly broken.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.WorkforceTrackShift)]
    [ProducesResponseType(typeof(QuickWorkDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Start([FromBody] StartQuickWorkDto request, CancellationToken ct)
        => FromResult(await _quickWork.StartAsync(CurrentUserId, request, ct));

    /// <summary>Stop the clock, record what came of it, and optionally pick the task back up.</summary>
    [HttpPost("{id:long}/finish")]
    [ProducesResponseType(typeof(QuickWorkDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Finish(
        long id, [FromBody] FinishQuickWorkDto request, CancellationToken ct)
        => FromResult(await _quickWork.FinishAsync(id, CurrentUserId, request, ct));

    /// <summary>Started by mistake. Kept as a record, not counted as productive time.</summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(typeof(QuickWorkDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
        => FromResult(await _quickWork.CancelAsync(id, CurrentUserId, ct));

    /// <summary>
    /// It turned out to be real work. Raises a <b>request</b> — not a task: approval is what creates
    /// work, here as everywhere else.
    /// </summary>
    [HttpPost("{id:long}/promote")]
    [HasPermission(Permissions.RequestCreate)]
    [ProducesResponseType(typeof(QuickWorkDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Promote(
        long id, [FromBody] PromoteQuickWorkDto request, CancellationToken ct)
        => FromResult(await _quickWork.PromoteAsync(id, CurrentUserId, request, ct));
}
