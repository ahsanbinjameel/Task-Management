using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Api.Services;
using WorkflowApp.Application.Reporting;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// One dashboard per audience. The three personal ones are scoped to the caller's own id — there is
/// no user parameter to tamper with — while the coordinator and management views are gated on the
/// permissions that make them somebody's job.
/// </summary>
[Route("api/dashboards")]
public sealed class DashboardsController : ApiControllerBase
{
    private readonly IDashboardService _dashboards;

    public DashboardsController(IDashboardService dashboards) => _dashboards = dashboards;

    /// <summary>
    /// What is waiting on the caller, and what has happened around their work. Two lists rather
    /// than one: a screen that mixes "you must do this" with "this happened" makes the reader sort
    /// them, every visit. Scoped by the caller's own permissions, taken from the token.
    /// </summary>
    [HttpGet("home")]
    [ProducesResponseType(typeof(HomeDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Home(CancellationToken ct)
        => Ok(await _dashboards.HomeAsync(CurrentUserId, CurrentPermissions, ct));

    /// <summary>Where the caller's own requests got to.</summary>
    [HttpGet("requester")]
    [ProducesResponseType(typeof(RequesterDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Requester(CancellationToken ct)
        => Ok(await _dashboards.RequesterAsync(CurrentUserId, ct));

    /// <summary>What is on the caller today.</summary>
    [HttpGet("worker")]
    [ProducesResponseType(typeof(WorkerDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Worker(CancellationToken ct)
        => Ok(await _dashboards.WorkerAsync(CurrentUserId, ct));

    /// <summary>What is unassigned, stuck or late.</summary>
    [HttpGet("coordinator")]
    [HasPermission(Permissions.TaskAssign)]
    [ProducesResponseType(typeof(CoordinatorDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Coordinator(CancellationToken ct)
        => Ok(await _dashboards.CoordinatorAsync(ct));

    /// <summary>Throughput, QC pass rate and cycle time over a window. Defaults to the last 30 days.</summary>
    [HttpGet("management")]
    [HasPermission(Permissions.DashboardManagement)]
    [ProducesResponseType(typeof(ManagementDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Management(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct = default)
    {
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? end.AddDays(-30);

        if (start > end)
            return Problem(Application.Common.Models.Error.Validation(
                "report.invalid_range", "'from' must not be after 'to'."));

        return Ok(await _dashboards.ManagementAsync(start, end, ct));
    }
}

/// <summary>Daily attendance and effort reports, plus a CSV of the same figures.</summary>
[Route("api/reports")]
public sealed class ReportsController : ApiControllerBase
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    /// <summary>The caller's own day. Deliberately ungated — anyone may see their own hours.</summary>
    [HttpGet("me/daily")]
    [ProducesResponseType(typeof(DailyUserReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> MyDaily([FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _reports.DailyUserAsync(CurrentUserId, DayOrToday(date), ct));

    [HttpGet("users/{userId:long}/daily")]
    [HasPermission(Permissions.ReportsView)]
    [ProducesResponseType(typeof(DailyUserReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UserDaily(long userId, [FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _reports.DailyUserAsync(userId, DayOrToday(date), ct));

    [HttpGet("team/daily")]
    [HasPermission(Permissions.ReportsView)]
    [ProducesResponseType(typeof(DailyTeamReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> TeamDaily([FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _reports.DailyTeamAsync(DayOrToday(date), ct));

    [HttpGet("team/daily.csv")]
    [HasPermission(Permissions.ReportsView)]
    [Produces("text/csv")]
    public async Task<IActionResult> TeamDailyCsv([FromQuery] DateOnly? date, CancellationToken ct)
    {
        var day = DayOrToday(date);
        var csv = await _reports.DailyTeamCsvAsync(day, ct);

        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"team-daily-{day:yyyy-MM-dd}.csv");
    }

    /// <summary>
    /// The requested day, or today. "Today" comes from <see cref="ApiControllerBase.Today"/>, which
    /// resolves it in the configured business time zone — this used to default to UTC, which put an
    /// evening shift's hours on tomorrow's report for anyone east of Greenwich.
    /// </summary>
    /// <summary>
    /// The same day, as a document rather than a spreadsheet. The CSV is one flat row per person,
    /// which is what makes it the wrong shape to read: a day's quick work is a variable number of
    /// lines and a row cannot hold them.
    /// </summary>
    [HttpGet("team/daily.pdf")]
    [HasPermission(Permissions.ReportsView)]
    [Produces("application/pdf")]
    public async Task<IActionResult> TeamDailyPdf(
        [FromQuery] DateOnly? date, [FromServices] IDailyReportPdf pdf, CancellationToken ct)
    {
        var day = DayOrToday(date);
        var report = await _reports.DailyTeamAsync(day, ct);

        return File(pdf.Team(report), "application/pdf", $"team-daily-{day:yyyy-MM-dd}.pdf");
    }

    /// <summary>The caller's own day as a document. Ungated, like the JSON version of the same thing.</summary>
    [HttpGet("me/daily.pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> MyDailyPdf(
        [FromQuery] DateOnly? date, [FromServices] IDailyReportPdf pdf, CancellationToken ct)
    {
        var day = DayOrToday(date);
        var report = await _reports.DailyUserAsync(CurrentUserId, day, ct);

        return File(pdf.Person(report), "application/pdf", $"my-day-{day:yyyy-MM-dd}.pdf");
    }

    [HttpGet("users/{userId:long}/daily.pdf")]
    [HasPermission(Permissions.ReportsView)]
    [Produces("application/pdf")]
    public async Task<IActionResult> UserDailyPdf(
        long userId, [FromQuery] DateOnly? date, [FromServices] IDailyReportPdf pdf, CancellationToken ct)
    {
        var day = DayOrToday(date);
        var report = await _reports.DailyUserAsync(userId, day, ct);

        return File(pdf.Person(report), "application/pdf",
            $"{report.DisplayName}-{day:yyyy-MM-dd}.pdf".Replace(' ', '-'));
    }

    private DateOnly DayOrToday(DateOnly? date) => date ?? Today;
}
