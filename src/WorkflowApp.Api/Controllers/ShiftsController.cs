using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Workforce.Dtos;
using WorkflowApp.Application.Workforce.Services;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// Self-service shift and availability. Every route acts on the caller's own record — the user id
/// comes from the token, never from the request. Acting on someone else lives in
/// <see cref="WorkforceController"/>.
///
/// Only <c>Workforce.TrackShift</c> holders are on the clock, so only they can open a shift.
/// Closing one and changing availability are deliberately not gated: both already require an open
/// shift, and someone whose permission is revoked mid-shift must still be able to clock out
/// without needing a supervisor.
/// </summary>
public sealed class ShiftsController : ApiControllerBase
{
    private readonly IShiftService _shifts;
    private readonly IWorkforceQueryService _queries;

    public ShiftsController(IShiftService shifts, IWorkforceQueryService queries)
    {
        _shifts = shifts;
        _queries = queries;
    }

    /// <summary>The caller's current workforce state, open shift, and the states they may switch to.</summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(WorkforceStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Current(CancellationToken ct)
        => FromResult(await _shifts.GetStatusAsync(CurrentUserId, ct));

    /// <summary>
    /// Opens a shift. Restricted to people whose attendance is tracked; fails if one is already
    /// open, since only one may be open at a time.
    /// </summary>
    [HttpPost("start")]
    [HasPermission(Permissions.WorkforceTrackShift)]
    [ProducesResponseType(typeof(WorkforceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(CancellationToken ct)
        => FromResult(await _shifts.StartShiftAsync(CurrentUserId, ct));

    /// <summary>Closes the caller's shift. Refused while a task work session is still running.</summary>
    [HttpPost("end")]
    [ProducesResponseType(typeof(WorkforceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> End([FromBody] EndShiftRequest? request, CancellationToken ct)
        => FromResult(await _shifts.EndShiftAsync(CurrentUserId, request?.Note, ct));

    /// <summary>
    /// Moves the caller between availability states (Available / Break / Lunch / Meeting /
    /// TemporarilyAway). Working and ShiftEnded are not settable here by design.
    /// </summary>
    [HttpPut("state")]
    [ProducesResponseType(typeof(WorkforceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeState([FromBody] ChangeWorkforceStateRequest request, CancellationToken ct)
        => FromResult(await _shifts.ChangeStateAsync(CurrentUserId, request.State, request.Note, ct));

    /// <summary>The caller's timeline for a business day. Defaults to today.</summary>
    [HttpGet("timeline")]
    [ProducesResponseType(typeof(DailyTimelineDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Timeline([FromQuery] DateOnly? date, CancellationToken ct)
        => FromResult(await _queries.GetDailyTimelineAsync(CurrentUserId, date ?? Today, ct));

    /// <summary>The caller's raw activity events for a business day.</summary>
    [HttpGet("activity")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityEventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Activity([FromQuery] DateOnly? date, CancellationToken ct)
        => FromResult(await _queries.GetActivityAsync(CurrentUserId, date ?? Today, ct));

    /// <summary>The caller's past shifts, newest first.</summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(PagedResult<ShiftSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> History(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
        => FromResult(await _queries.GetShiftHistoryAsync(
            CurrentUserId, from, to, new PageQuery { Page = page, PageSize = pageSize }, ct));
}
