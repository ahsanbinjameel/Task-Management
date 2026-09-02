using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Reporting;

public interface IDashboardService
{
    /// <summary>
    /// The two lists the home screen leads with: what is waiting on this caller, and what has
    /// happened around their work. Scoped by <paramref name="permissions"/> rather than by a role
    /// name, the same way <see cref="Common.StatusViews"/> resolves its audience.
    /// </summary>
    Task<HomeDashboardDto> HomeAsync(
        long userId, IReadOnlySet<string> permissions, CancellationToken ct = default);

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

    // --- home: needs attention / recent activity -------------------------------------------

    /// <summary>How many rows each list carries. Beyond this the page says "and N more".</summary>
    private const int AttentionLimit = 12;
    private const int ActivityLimit = 15;

    public async Task<HomeDashboardDto> HomeAsync(
        long userId, IReadOnlySet<string> permissions, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var canAssign = permissions.Contains(Permissions.TaskAssign);
        var canReview = permissions.Contains(Permissions.TaskReview);
        var canCheck = permissions.Contains(Permissions.TaskQCReview);
        var canClose = permissions.Contains(Permissions.TaskClose);
        var canWork = permissions.Contains(Permissions.TaskWork);

        var items = new List<AttentionItemDto>();

        // Everything below reads open tasks once. Separate filtered queries would each be cheaper,
        // but they would also be several round trips for a screen that loads on every visit.
        var openTasks = await _db.Tasks.AsNoTracking()
            .Where(t => !TerminalStatuses.Contains(t.Status))
            .Select(t => new OpenTaskRow(
                t.Id, t.TaskNumber, t.Title, t.Status, t.Priority, t.DueDate,
                t.PrimaryAssigneeUserId, t.CreatedAt))
            .ToListAsync(ct);

        // "Waiting since" is the status-history row that put the task where it is, not UpdatedAt —
        // a comment added this morning must not reset a task that has sat unassigned for a week.
        var openIds = openTasks.Select(t => t.Id).ToList();
        var enteredAt = await _db.StatusHistories.AsNoTracking()
            .Where(h => openIds.Contains(h.TaskId))
            .GroupBy(h => h.TaskId)
            .Select(g => new { TaskId = g.Key, At = g.Max(h => h.ChangedAt) })
            .ToDictionaryAsync(x => x.TaskId, x => x.At, ct);

        void AddTask(OpenTaskRow t, string reason, int rank) =>
            items.Add(new AttentionItemDto(
                AttentionSubject.Task, t.Id, t.TaskNumber, t.Title, reason, rank,
                t.Priority,
                enteredAt.TryGetValue(t.Id, out var at) ? at : t.CreatedAt,
                t.DueDate,
                t.DueDate is { } due && due < now));

        // Ranks are the order the sections read in, not a severity score: the caller's own overdue
        // and returned work first, then their queue, then work that is nobody's until they act.
        if (canWork)
        {
            foreach (var t in openTasks.Where(t => t.AssigneeUserId == userId))
            {
                if (t.Status == WorkTaskStatus.QCFailedRework)
                    AddTask(t, "Came back from the quality check and needs fixing", 0);
                else if (t.DueDate is { } due && due < now)
                    AddTask(t, "Overdue", 1);
                else if (t.Status is WorkTaskStatus.Assigned or WorkTaskStatus.ReadyToStart)
                    AddTask(t, "Yours to start", 3);
                // The commonest state for a worker's own work, and it was the one state missing:
                // everything they put down for a phone call, a meeting or a more urgent job lands
                // here. Leaving it out meant a worker with three paused tasks was told nothing was
                // waiting on them.
                else if (t.Status == WorkTaskStatus.Paused)
                    AddTask(t, "Paused - yours to pick back up", 3);
                else if (t.Status == WorkTaskStatus.Blocked)
                    AddTask(t, "Cannot continue - see why on the task", 3);
            }
        }

        if (canReview)
        {
            var toReview = await _db.Requests.AsNoTracking()
                .Where(r => r.Status == RequestStatus.Submitted || r.Status == RequestStatus.InReview)
                .Select(r => new
                {
                    r.Id, r.RequestNumber, r.Title, r.RequestedUrgency, r.TargetDate, r.CreatedAt, r.Status,
                })
                .ToListAsync(ct);

            // Answering a question sends the request straight back to Submitted, so it is already
            // in the list above. What it is not is a *new* request: it is one this reviewer is
            // personally waiting on, and saying so is the difference between the row being noticed
            // and being lost among twenty others.
            var reviewIds = toReview.Select(r => r.Id).ToList();
            var answeredMine = await _db.RequestClarifications.AsNoTracking()
                .Where(c => c.AskedByUserId == userId
                            && c.AnsweredAt != null
                            && reviewIds.Contains(c.RequestId))
                .GroupBy(c => c.RequestId)
                .Select(g => new { RequestId = g.Key, At = g.Max(c => c.AnsweredAt!.Value) })
                .ToDictionaryAsync(x => x.RequestId, x => x.At, ct);

            items.AddRange(toReview.Select(r =>
            {
                var replied = answeredMine.TryGetValue(r.Id, out var answeredAt);

                return new AttentionItemDto(
                    AttentionSubject.Request, r.Id, r.RequestNumber, r.Title,
                    replied ? "The requester answered your question"
                        : r.Status == RequestStatus.Submitted ? "Waiting for a review"
                        : "You started reviewing this",
                    replied ? 1 : 2,
                    (Priority)(int)r.RequestedUrgency,
                    replied ? answeredAt : r.CreatedAt,
                    r.TargetDate,
                    r.TargetDate is { } reviewDue && reviewDue < now);
            }));
        }

        if (canAssign)
        {
            foreach (var t in openTasks.Where(t => t.Status == WorkTaskStatus.ReadyForAssignment))
                AddTask(t, "Needs someone to do it", 2);

            foreach (var t in openTasks.Where(t =>
                         t.Status == WorkTaskStatus.Blocked && t.AssigneeUserId != userId))
                AddTask(t, "Stuck - someone needs to unblock it", 4);
        }

        if (canCheck)
        {
            foreach (var t in openTasks.Where(t => t.Status == WorkTaskStatus.CompletedReadyForQC))
                AddTask(t, "Waiting for a quality check", 2);
        }

        if (canClose)
        {
            foreach (var t in openTasks.Where(t =>
                         t.Status is WorkTaskStatus.QCPassed or WorkTaskStatus.ReadyForClosure))
                AddTask(t, "Passed its check and can be closed", 4);
        }

        // Everyone, whatever else they do: the one thing only the person who asked can answer
        // (PRODUCT-CORE §7). Deliberately not gated on a permission — whether the thing is really
        // fixed is not a question authority answers — so it appears for whoever raised the request
        // and for nobody else, the coordinator who raised one of their own included.
        //
        // Keyed off the task passing its check, not off anyone shifting it on to ReadyForClosure:
        // that shift is a coordinator's housekeeping and the requester was never waiting on it.
        var awaitingConfirmation = openTasks
            .Where(t => t.Status is WorkTaskStatus.QCPassed or WorkTaskStatus.ReadyForClosure)
            .Select(t => t.Id)
            .ToList();

        var mineToConfirm = await _db.Requests.AsNoTracking()
            .Where(r => r.RequestedByUserId == userId
                        && r.GeneratedTaskId != null
                        && awaitingConfirmation.Contains(r.GeneratedTaskId!.Value))
            .Select(r => new
            {
                r.Id, r.RequestNumber, r.Title, r.RequestedUrgency, r.TargetDate, r.GeneratedTaskId,
            })
            .ToListAsync(ct);

        // The row points at the request, not the task: that is where the two buttons live, and a
        // requester is never sent to the task screen.
        items.AddRange(mineToConfirm.Select(r => new AttentionItemDto(
            AttentionSubject.Request, r.Id, r.RequestNumber, r.Title,
            "Waiting for you to confirm it is fixed", 0, (Priority)(int)r.RequestedUrgency,
            enteredAt.TryGetValue(r.GeneratedTaskId!.Value, out var passedAt) ? passedAt : now,
            r.TargetDate, false)));

        // Everyone, whatever else they do: a question addressed to them on something they raised.
        var myQuestions = await _db.RequestClarifications.AsNoTracking()
            .Where(c => c.AnsweredAt == null)
            .Join(_db.Requests.AsNoTracking().Where(r => r.RequestedByUserId == userId),
                c => c.RequestId, r => r.Id,
                (c, r) => new { r.Id, r.RequestNumber, r.Title, r.RequestedUrgency, r.TargetDate, c.AskedAt })
            .ToListAsync(ct);

        items.AddRange(myQuestions.Select(q => new AttentionItemDto(
            AttentionSubject.Request, q.Id, q.RequestNumber, q.Title,
            "A reviewer asked you a question", 0, (Priority)(int)q.RequestedUrgency,
            q.AskedAt, q.TargetDate, false)));

        // The same task can qualify twice - an overdue task a coordinator also has to unblock. The
        // strongest reason wins, so the row is never duplicated with two different explanations.
        var deduped = items
            .GroupBy(i => (i.Subject, i.Id))
            .Select(g => g.OrderBy(i => i.Rank).First())
            .OrderBy(i => i.Rank)
            .ThenByDescending(i => i.IsOverdue)
            .ThenBy(i => i.Priority)
            .ThenBy(i => i.Since)
            .ToList();

        var seesEverything = canAssign || canReview || canCheck
            || permissions.Contains(Permissions.ReportsView);

        return new HomeDashboardDto(
            deduped.Take(AttentionLimit).ToList(),
            await RecentActivityAsync(userId, seesEverything, ct),
            deduped.Count);
    }

    /// <summary>
    /// What happened lately, past tense. Coordinators see the whole floor because that is their
    /// job; everyone else sees only activity on work they are part of - theirs to do, theirs to
    /// help with, or from a request they raised. Same rule as the task list, for the same reason:
    /// a feed that leaked other people's work would be a permission hole with a friendly face.
    /// </summary>
    private async Task<IReadOnlyList<ActivityItemDto>> RecentActivityAsync(
        long userId, bool seesEverything, CancellationToken ct)
    {
        var taskActivity = _db.TaskActivities.AsNoTracking();
        var requestActivity = _db.RequestActivities.AsNoTracking();

        if (!seesEverything)
        {
            taskActivity = taskActivity.Where(a => _db.Tasks.Any(t =>
                t.Id == a.TaskId
                && (t.PrimaryAssigneeUserId == userId
                    || _db.TaskCollaborators.Any(c => c.TaskId == t.Id && c.UserId == userId)
                    || (t.RequestId != null
                        && _db.Requests.Any(r => r.Id == t.RequestId && r.RequestedByUserId == userId)))));

            requestActivity = requestActivity.Where(a =>
                _db.Requests.Any(r => r.Id == a.RequestId && r.RequestedByUserId == userId));
        }

        var tasks = await taskActivity
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Take(ActivityLimit)
            .Join(_db.Tasks.AsNoTracking(), a => a.TaskId, t => t.Id, (a, t) => new ActivityItemDto(
                AttentionSubject.Task, t.Id, t.TaskNumber, a.Description, a.OccurredAt))
            .ToListAsync(ct);

        var requests = await requestActivity
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Take(ActivityLimit)
            .Join(_db.Requests.AsNoTracking(), a => a.RequestId, r => r.Id, (a, r) => new ActivityItemDto(
                AttentionSubject.Request, r.Id, r.RequestNumber, a.Description, a.OccurredAt))
            .ToListAsync(ct);

        return tasks.Concat(requests)
            .OrderByDescending(a => a.At)
            .Take(ActivityLimit)
            .ToList();
    }

    /// <summary>The columns the attention scan needs, named so the local helper is not `dynamic`.</summary>
    private sealed record OpenTaskRow(
        long Id, string TaskNumber, string Title, WorkTaskStatus Status, Priority Priority,
        DateTimeOffset? DueDate, long? AssigneeUserId, DateTimeOffset CreatedAt);

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
