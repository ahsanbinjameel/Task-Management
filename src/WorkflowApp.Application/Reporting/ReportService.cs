using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Workforce.Services;
using WorkflowApp.Domain.Entities.Workforce;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Reporting;

public interface IReportService
{
    Task<DailyUserReportDto> DailyUserAsync(long userId, DateOnly date, CancellationToken ct = default);

    /// <summary>Everyone who was on shift that day, plus the totals across them.</summary>
    Task<DailyTeamReportDto> DailyTeamAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>The team report as CSV, for the spreadsheet everybody inevitably wants.</summary>
    Task<string> DailyTeamCsvAsync(DateOnly date, CancellationToken ct = default);
}

/// <summary>
/// Daily attendance and effort reports.
///
/// The phase plan called for stored procedures. These are EF queries instead, deliberately: the
/// schema then has exactly one definition (the model), the reports are covered by the same test
/// suite as everything else with no SQL Server required, and there is no second artefact to keep in
/// step through a migration. At this data scale the query cost is not the constraint. If a report
/// ever does outgrow this, the place to fix it is here, behind the interface.
///
/// Attendance comes from the activity stream via <see cref="DailyTimelineBuilder"/> — the same
/// calculation the workforce screens use, so a report and the timeline can never disagree.
/// </summary>
public sealed class ReportService : IReportService
{
    /// <summary>Statuses that mean the task is finished with, one way or another.</summary>
    private static readonly WorkTaskStatus[] TerminalStatuses =
    {
        WorkTaskStatus.Closed, WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate
    };

    private readonly IWorkflowDbContext _db;
    private readonly IBusinessCalendar _calendar;
    private readonly IDateTimeProvider _clock;

    public ReportService(IWorkflowDbContext db, IBusinessCalendar calendar, IDateTimeProvider clock)
    {
        _db = db;
        _calendar = calendar;
        _clock = clock;
    }

    public async Task<DailyUserReportDto> DailyUserAsync(
        long userId, DateOnly date, CancellationToken ct = default)
    {
        var (dayStart, dayEnd) = _calendar.DayRange(date);

        var displayName = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? $"User {userId}";

        var events = await _db.ActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAt >= dayStart && e.OccurredAt < dayEnd)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .ToListAsync(ct);

        // The state carried in from before midnight — without it an overnight shift loses its
        // first hours. Same rule the Phase 2 timeline follows.
        var carried = await _db.ActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAt < dayStart && e.ResultingState != null)
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync(ct);

        var (_, onShift, productive, away, _) =
            DailyTimelineBuilder.Build(events, dayStart, dayEnd, _clock.UtcNow, carried);

        var shift = await _db.ShiftSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.ShiftStart < dayEnd
                        && (s.ShiftEnd == null || s.ShiftEnd > dayStart))
            .OrderBy(s => s.ShiftStart)
            .FirstOrDefaultAsync(ct);

        var breakdown = await TaskBreakdownAsync(userId, dayStart, dayEnd, ct);

        // Split by who is actually responsible. Time is time, but the report must not imply this
        // person owns work they were only helping with.
        var workedTaskIds = breakdown.Select(b => b.TaskId).ToList();
        var ownedTaskIds = await _db.Tasks.AsNoTracking()
            .Where(t => workedTaskIds.Contains(t.Id) && t.PrimaryAssigneeUserId == userId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var ownedWork = breakdown.Where(b => ownedTaskIds.Contains(b.TaskId)).ToList();
        var supportWork = breakdown.Where(b => !ownedTaskIds.Contains(b.TaskId)).ToList();

        var supportingOn = await _db.TaskCollaborators.AsNoTracking()
            .Where(c => c.UserId == userId && !TerminalStatuses.Contains(c.Task.Status))
            .OrderBy(c => c.Task.TaskNumber)
            .Select(c => new SupportedTaskDto(
                c.Task.Id, c.Task.TaskNumber, c.Task.Title, c.Task.Status.ToString(),
                c.Task.PrimaryAssigneeUser != null ? c.Task.PrimaryAssigneeUser.DisplayName : null))
            .ToListAsync(ct);

        var completed = await _db.StatusHistories.AsNoTracking()
            .CountAsync(h => h.ChangedByUserId == userId
                             && h.ToStatus == WorkTaskStatus.CompletedReadyForQC
                             && h.ChangedAt >= dayStart && h.ChangedAt < dayEnd, ct);

        return new DailyUserReportDto(
            date, userId, displayName,
            shift?.ShiftStart, shift?.ShiftEnd, onShift, productive, away,
            // "Worked" counts only what they are responsible for; support is reported on its own.
            ownedWork.Count, completed, ownedWork, supportWork, supportingOn);
    }

    public async Task<DailyTeamReportDto> DailyTeamAsync(DateOnly date, CancellationToken ct = default)
    {
        var (dayStart, dayEnd) = _calendar.DayRange(date);

        var userIds = await _db.ShiftSessions.AsNoTracking()
            .Where(s => s.ShiftStart < dayEnd && (s.ShiftEnd == null || s.ShiftEnd > dayStart))
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync(ct);

        var users = new List<DailyUserReportDto>();
        foreach (var userId in userIds)
            users.Add(await DailyUserAsync(userId, date, ct));

        users = users.OrderBy(u => u.DisplayName).ToList();

        return new DailyTeamReportDto(
            date,
            users.Count,
            users.Aggregate(TimeSpan.Zero, (sum, u) => sum + u.ShiftDuration),
            users.Aggregate(TimeSpan.Zero, (sum, u) => sum + u.ProductiveTime),
            users.Sum(u => u.TasksCompleted),
            users);
    }

    public async Task<string> DailyTeamCsvAsync(DateOnly date, CancellationToken ct = default)
    {
        var report = await DailyTeamAsync(date, ct);

        var csv = new StringBuilder();
        csv.AppendLine("Date,User,ShiftStart,ShiftEnd,ShiftHours,ProductiveHours,BreakHours,TasksWorked,TasksCompleted");

        foreach (var u in report.Users)
        {
            csv.AppendLine(string.Join(",",
                Csv(report.Date.ToString("yyyy-MM-dd")),
                Csv(u.DisplayName),
                Csv(u.ShiftStart?.ToString("O") ?? string.Empty),
                Csv(u.ShiftEnd?.ToString("O") ?? string.Empty),
                Hours(u.ShiftDuration),
                Hours(u.ProductiveTime),
                Hours(u.BreakTime),
                u.TasksWorked.ToString(CultureInfo.InvariantCulture),
                u.TasksCompleted.ToString(CultureInfo.InvariantCulture)));
        }

        return csv.ToString();
    }

    // --- helpers -------------------------------------------------------------------------

    /// <summary>Time spent per task that day, from closed work sessions clipped to the day.</summary>
    private async Task<IReadOnlyList<TaskTimeDto>> TaskBreakdownAsync(
        long userId, DateTimeOffset dayStart, DateTimeOffset dayEnd, CancellationToken ct)
    {
        var sessions = await _db.WorkSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.SessionEnd != null
                        && s.SessionEnd > dayStart && s.SessionStart < dayEnd)
            .Select(s => new { s.TaskId, s.SessionStart, s.SessionEnd })
            .ToListAsync(ct);

        if (sessions.Count == 0) return Array.Empty<TaskTimeDto>();

        var taskIds = sessions.Select(s => s.TaskId).Distinct().ToList();
        var tasks = await _db.Tasks.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.TaskNumber, t.Title })
            .ToDictionaryAsync(t => t.Id, t => t, ct);

        return sessions
            .GroupBy(s => s.TaskId)
            .Select(g => new TaskTimeDto(
                g.Key,
                tasks.TryGetValue(g.Key, out var t) ? t.TaskNumber : "(deleted)",
                tasks.TryGetValue(g.Key, out var t2) ? t2.Title : "(deleted)",
                g.Aggregate(TimeSpan.Zero, (sum, s) =>
                    sum + (Min(s.SessionEnd!.Value, dayEnd) - Max(s.SessionStart, dayStart))),
                g.Count()))
            .OrderByDescending(t => t.TimeSpent)
            .ToList();
    }

    private static string Hours(TimeSpan span) =>
        Math.Round(span.TotalHours, 2).ToString(CultureInfo.InvariantCulture);

    /// <summary>Quotes a CSV field. Excel treats a bare comma or quote as structure otherwise.</summary>
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
