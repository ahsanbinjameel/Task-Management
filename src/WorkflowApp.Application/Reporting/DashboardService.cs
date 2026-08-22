using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Reporting;

public interface IDashboardService
{
    Task<RequesterDashboardDto> RequesterAsync(long userId, CancellationToken ct = default);
    Task<WorkerDashboardDto> WorkerAsync(long userId, CancellationToken ct = default);
    Task<CoordinatorDashboardDto> CoordinatorAsync(CancellationToken ct = default);
    Task<ManagementDashboardDto> ManagementAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>
/// Four dashboards, one per audience, because "a dashboard" that tries to serve everybody serves
/// nobody. A requester wants to know where their ask got to; a worker wants to know what is on them
/// today; a coordinator wants to see what is unassigned, stuck or late; management wants to know
/// whether the system is keeping up.
///
/// Every figure here is derived from the same tables the operational screens read. There is no
/// separate reporting store to fall out of step, which matters more than the query cost at this
/// scale.
/// </summary>
public sealed class DashboardService : IDashboardService
{
    private static readonly WorkTaskStatus[] TerminalStatuses =
    {
        WorkTaskStatus.Closed, WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate
    };

    private readonly IWorkflowDbContext _db;
    private readonly IBusinessCalendar _calendar;
    private readonly IDateTimeProvider _clock;

    public DashboardService(IWorkflowDbContext db, IBusinessCalendar calendar, IDateTimeProvider clock)
    {
        _db = db;
        _calendar = calendar;
        _clock = clock;
    }

    public async Task<RequesterDashboardDto> RequesterAsync(long userId, CancellationToken ct = default)
    {
        var mine = await _db.Requests.AsNoTracking()
            .Where(r => r.RequestedByUserId == userId)
            .Select(r => new { r.Id, r.RequestNumber, r.Title, r.Status, r.RequestedUrgency, r.TargetDate, r.CreatedAt })
            .ToListAsync(ct);

        var now = _clock.UtcNow;

        // A request has no "closed" state of its own — it ends by being rejected, marked duplicate,
        // or approved into a task. So progress and completion are read from the generated task.
        var requestIds = mine.Select(r => r.Id).ToList();

        var taskStatuses = await _db.Tasks.AsNoTracking()
            .Where(t => t.RequestId != null && requestIds.Contains(t.RequestId.Value))
            .Select(t => t.Status)
            .ToListAsync(ct);

        return new RequesterDashboardDto(
            mine.Count(r => r.Status == RequestStatus.Submitted),
            mine.Count(r => r.Status == RequestStatus.InReview),
            mine.Count(r => r.Status == RequestStatus.ClarificationRequired),
            taskStatuses.Count(s => !TerminalStatuses.Contains(s)),
            taskStatuses.Count(s => s == WorkTaskStatus.Closed),
            mine.Count(r => r.Status is RequestStatus.Rejected or RequestStatus.Duplicate),
            mine.OrderByDescending(r => r.CreatedAt).Take(10)
                .Select(r => new DashboardItemDto(
                    r.Id, r.RequestNumber, r.Title, r.Status.ToString(),
                    (Priority)(int)r.RequestedUrgency, r.TargetDate,
                    r.TargetDate is { } due && due < now
                        && r.Status is not (RequestStatus.Rejected or RequestStatus.Duplicate)))
                .ToList());
    }

    public async Task<WorkerDashboardDto> WorkerAsync(long userId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var (dayStart, dayEnd) = _calendar.DayRange(_calendar.ToBusinessDate(now));

        var mine = await _db.Tasks.AsNoTracking()
            .Where(t => t.PrimaryAssigneeUserId == userId && !TerminalStatuses.Contains(t.Status))
            .OrderBy(t => t.QueueOrder).ThenBy(t => t.Priority).ThenBy(t => t.Id)
            .Select(t => new { t.Id, t.TaskNumber, t.Title, t.Status, t.Priority, t.DueDate })
            .ToListAsync(ct);

        var active = await _db.WorkSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == WorkSessionStatus.Active)
            .Join(_db.Tasks.AsNoTracking(), s => s.TaskId, t => t.Id,
                (s, t) => new { t.Id, t.TaskNumber })
            .FirstOrDefaultAsync(ct);

        // Closed sessions overlapping today, clipped to the business day so an overnight shift
        // reports against the right date on both sides of midnight.
        var sessions = await _db.WorkSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.SessionEnd != null
                        && s.SessionEnd > dayStart && s.SessionStart < dayEnd)
            .Select(s => new { s.SessionStart, s.SessionEnd })
            .ToListAsync(ct);

        var workedToday = sessions.Aggregate(TimeSpan.Zero, (sum, s) =>
            sum + (Min(s.SessionEnd!.Value, dayEnd) - Max(s.SessionStart, dayStart)));

        return new WorkerDashboardDto(
            mine.Count,
            mine.Count(t => t.Status == WorkTaskStatus.InProgress),
            mine.Count(t => t.Status == WorkTaskStatus.Blocked),
            mine.Count(t => t.Status == WorkTaskStatus.QCFailedRework),
            mine.Count(t => t.DueDate is { } due && due < now),
            active?.Id,
            active?.TaskNumber,
            await _db.ShiftSessions.AsNoTracking().AnyAsync(s => s.UserId == userId && s.ShiftEnd == null, ct),
            workedToday,
            await _db.Notifications.AsNoTracking().CountAsync(n => n.RecipientUserId == userId && !n.IsRead, ct),
            mine.Take(10).Select(t => Item(t.Id, t.TaskNumber, t.Title, t.Status, t.Priority, t.DueDate, now)).ToList());
    }

    public async Task<CoordinatorDashboardDto> CoordinatorAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var open = await _db.Tasks.AsNoTracking()
            .Where(t => !TerminalStatuses.Contains(t.Status))
            .Select(t => new { t.Id, t.TaskNumber, t.Title, t.Status, t.Priority, t.DueDate, t.PrimaryAssigneeUserId })
            .ToListAsync(ct);

        var overdue = open.Where(t => t.DueDate is { } due && due < now)
            .OrderBy(t => t.DueDate).ToList();

        var unassigned = open.Where(t => t.Status == WorkTaskStatus.ReadyForAssignment)
            .OrderBy(t => t.Priority).ThenBy(t => t.DueDate ?? DateTimeOffset.MaxValue).ToList();

        return new CoordinatorDashboardDto(
            await _db.Requests.AsNoTracking().CountAsync(
                r => r.Status == RequestStatus.Submitted || r.Status == RequestStatus.InReview, ct),
            unassigned.Count,
            open.Count(t => t.Status == WorkTaskStatus.Blocked),
            open.Count(t => t.Status is WorkTaskStatus.CompletedReadyForQC or WorkTaskStatus.QCReview),
            overdue.Count,
            await _db.ShiftSessions.AsNoTracking().CountAsync(s => s.ShiftEnd == null, ct),
            await _db.Users.AsNoTracking().CountAsync(u => u.WorkforceState == WorkforceState.Working, ct),
            unassigned.Take(10).Select(t => Item(t.Id, t.TaskNumber, t.Title, t.Status, t.Priority, t.DueDate, now)).ToList(),
            overdue.Take(10).Select(t => Item(t.Id, t.TaskNumber, t.Title, t.Status, t.Priority, t.DueDate, now)).ToList());
    }

    public async Task<ManagementDashboardDto> ManagementAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var start = _calendar.DayRange(from).Start;
        var end = _calendar.DayRange(to).EndExclusive;

        var requestsRaised = await _db.Requests.AsNoTracking()
            .CountAsync(r => r.CreatedAt >= start && r.CreatedAt < end, ct);

        var tasksCreated = await _db.Tasks.AsNoTracking()
            .CountAsync(t => t.CreatedAt >= start && t.CreatedAt < end, ct);

        // Closures come from the status trail, not from the task row: a task closed last month and
        // reopened this one must not count twice in the same window.
        var closures = await _db.StatusHistories.AsNoTracking()
            .Where(h => h.ToStatus == WorkTaskStatus.Closed && h.ChangedAt >= start && h.ChangedAt < end)
            .Select(h => new { h.TaskId, h.ChangedAt })
            .ToListAsync(ct);

        var qc = await _db.QCReviews.AsNoTracking()
            .Where(q => q.ReviewedAt >= start && q.ReviewedAt < end)
            .Select(q => q.Result)
            .ToListAsync(ct);

        var sessions = await _db.WorkSessions.AsNoTracking()
            .Where(s => s.SessionEnd != null && s.SessionEnd > start && s.SessionStart < end)
            .Select(s => new { s.SessionStart, s.SessionEnd })
            .ToListAsync(ct);

        var hours = sessions.Aggregate(TimeSpan.Zero, (sum, s) =>
            sum + (Min(s.SessionEnd!.Value, end) - Max(s.SessionStart, start)));

        var closedIds = closures.Select(c => c.TaskId).Distinct().ToList();
        var closedTasks = await _db.Tasks.AsNoTracking()
            .Where(t => closedIds.Contains(t.Id))
            .Select(t => new { t.Id, t.CreatedAt, t.PrimaryAssigneeUserId })
            .ToListAsync(ct);

        // Cycle time: created to closed, in hours. The honest measure of "how long does work take".
        var cycleTimes = closures
            .Join(closedTasks, c => c.TaskId, t => t.Id, (c, t) => (c.ChangedAt - t.CreatedAt).TotalHours)
            .Where(h => h >= 0)
            .ToList();

        var assigneeIds = closedTasks.Where(t => t.PrimaryAssigneeUserId.HasValue)
            .Select(t => t.PrimaryAssigneeUserId!.Value).Distinct().ToList();

        var names = await _db.Users.AsNoTracking()
            .Where(u => assigneeIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var openTasks = await _db.Tasks.AsNoTracking()
            .Where(t => !TerminalStatuses.Contains(t.Status))
            .Select(t => new { t.Status, t.Priority, t.DueDate })
            .ToListAsync(ct);

        var qcFailures = qc.Count(r => r == QCResult.Failed);

        return new ManagementDashboardDto(
            from, to,
            requestsRaised,
            tasksCreated,
            closures.Select(c => c.TaskId).Distinct().Count(),
            qc.Count,
            qcFailures,
            qc.Count == 0 ? 0 : Math.Round((double)qc.Count(r => r == QCResult.Passed) / qc.Count, 4),
            cycleTimes.Count == 0 ? null : Math.Round(cycleTimes.Average(), 2),
            Math.Round((decimal)hours.TotalHours, 2),
            openTasks.Count,
            openTasks.Count(t => t.DueDate is { } due && due < now),
            openTasks.GroupBy(t => t.Status)
                .Select(g => new CountByLabelDto(g.Key.ToString(), g.Count()))
                .OrderByDescending(x => x.Count).ToList(),
            openTasks.GroupBy(t => t.Priority)
                .Select(g => new CountByLabelDto(g.Key.ToString(), g.Count()))
                .OrderBy(x => x.Label).ToList(),
            closedTasks.Where(t => t.PrimaryAssigneeUserId.HasValue)
                .GroupBy(t => t.PrimaryAssigneeUserId!.Value)
                .Select(g => new CountByLabelDto(
                    names.TryGetValue(g.Key, out var name) ? name : $"User {g.Key}", g.Count()))
                .OrderByDescending(x => x.Count).ToList());
    }

    // --- helpers -------------------------------------------------------------------------

    private static DashboardItemDto Item(
        long id, string number, string title, WorkTaskStatus status,
        Priority priority, DateTimeOffset? dueDate, DateTimeOffset now) =>
        new(id, number, title, status.ToString(), priority, dueDate, dueDate is { } due && due < now);

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
