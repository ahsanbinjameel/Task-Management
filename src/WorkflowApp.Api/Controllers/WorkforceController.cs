using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Workforce.Dtos;
using WorkflowApp.Application.Workforce.Services;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// Supervisory view of other people's shifts. Reading requires <c>Workforce.ViewAll</c>; changing
/// someone else's shift additionally requires <c>Workforce.ManageOthers</c>.
/// </summary>
[HasPermission(Permissions.WorkforceViewAll)]
public sealed class WorkforceController : ApiControllerBase
{
    private readonly IWorkforceQueryService _queries;
    private readonly IShiftService _shifts;

    public WorkforceController(IWorkforceQueryService queries, IShiftService shifts)
    {
        _queries = queries;
        _shifts = shifts;
    }

    /// <summary>Who is on shift right now, what state they are in, and for how long.</summary>
    [HttpGet("active")]
    [ProducesResponseType(typeof(ActiveWorkforceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Active(CancellationToken ct)
        => Ok(await _queries.GetActiveWorkforceAsync(ct));

    /// <summary>One user's current workforce status.</summary>
    [HttpGet("{userId:long}/status")]
    [ProducesResponseType(typeof(WorkforceStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Status(long userId, CancellationToken ct)
        => FromResult(await _shifts.GetStatusAsync(userId, ct));

    /// <summary>One user's timeline for a business day. Defaults to today.</summary>
    [HttpGet("{userId:long}/timeline")]
    [ProducesResponseType(typeof(DailyTimelineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Timeline(long userId, [FromQuery] DateOnly? date, CancellationToken ct)
        => FromResult(await _queries.GetDailyTimelineAsync(userId, date ?? Today, ct));

    /// <summary>One user's raw activity events for a business day.</summary>
    [HttpGet("{userId:long}/activity")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityEventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Activity(long userId, [FromQuery] DateOnly? date, CancellationToken ct)
        => FromResult(await _queries.GetActivityAsync(userId, date ?? Today, ct));

    /// <summary>One user's shift history, newest first.</summary>
    [HttpGet("{userId:long}/shifts")]
    [ProducesResponseType(typeof(PagedResult<ShiftSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Shifts(
        long userId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
        => FromResult(await _queries.GetShiftHistoryAsync(
            userId, from, to, new PageQuery { Page = page, PageSize = pageSize }, ct));

    /// <summary>
    /// Closes a shift the employee left open. Flagged as improperly ended and audited against the
    /// supervisor who did it; the reason is mandatory.
    /// </summary>
    [HttpPost("{userId:long}/end-shift")]
    [HasPermission(Permissions.WorkforceManageOthers)]
    [ProducesResponseType(typeof(ShiftSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ForceEndShift(
        long userId, [FromBody] ForceEndShiftRequest request, CancellationToken ct)
        => FromResult(await _shifts.ForceEndShiftAsync(userId, CurrentUserId, request.Reason, ct));
}
