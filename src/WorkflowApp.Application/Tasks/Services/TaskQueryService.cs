using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Workflow;

namespace WorkflowApp.Application.Tasks.Services;

/// <summary>Filters for listing tasks. All optional.</summary>
public sealed record TaskQuery
{
    public long? AssigneeUserId { get; init; }
    public WorkTaskStatus? Status { get; init; }

    /// <summary>
    /// The status group being looked at — "working", "waiting", "unassigned". Which internal
    /// statuses that covers depends on <see cref="Audience"/>; see <see cref="StatusViews"/>.
    /// Null or "all" means no status filter at all.
    /// </summary>
    public string? View { get; init; }

    /// <summary>
    /// Who is looking. Decides both the tiles offered and what each one contains, so a worker and
    /// a coordinator asking for "waiting" get the answer that is useful to each of them.
    /// </summary>
    public StatusAudience Audience { get; init; } = StatusAudience.Coordinator;
    public Priority? Priority { get; init; }
    public bool? Unassigned { get; init; }

    /// <summary>Only the children of this task.</summary>
    public long? ParentTaskId { get; init; }

    /// <summary>Only work for this client.</summary>
    public long? ClientId { get; init; }

    /// <summary>
    /// Restricts the list to work this person is actually part of — theirs to do, or theirs to
    /// help with. Set by the controller for anyone without a coordinating or reviewing role, so a
    /// worker browsing "Tasks" sees their own work rather than the whole organisation's.
    ///
    /// Visibility is not ownership: a task they only support appears here, and still never counts
    /// towards their queue, workload or task count.
    /// </summary>
    public long? VisibleToUserId { get; init; }
    public string? Search { get; init; }

    /// <summary>Excludes Closed / Cancelled / Duplicate — the default for working views.</summary>
    public bool OpenOnly { get; init; }

    /// <summary>
    /// Column to order by, as the client names it. Unknown values fall back to newest-first rather
    /// than throwing — a stale bookmark should show a sensible list, not an error.
    /// </summary>
    public string? SortBy { get; init; }

    public bool SortDescending { get; init; } = true;

    /// <summary>Per-column filters from the grid's filter row. See <see cref="ColumnFilters"/>.</summary>
    public ColumnFilters Columns { get; init; } = ColumnFilters.None;
}

public interface ITaskQueryService
{
    Task<Result<TaskDetailDto>> GetAsync(long taskId, CancellationToken ct = default);
    Task<PagedResult<TaskSummaryDto>> ListAsync(TaskQuery query, PageQuery page, CancellationToken ct = default);

    /// <summary>How many tasks sit in each status, under the same filters minus status.</summary>
    Task<IReadOnlyList<StatusCountDto>> StatusCountsAsync(TaskQuery query, CancellationToken ct = default);

    /// <summary>What each filterable column can still be narrowed by. See <see cref="FilterOptionsDto"/>.</summary>
    Task<FilterOptionsDto> FilterOptionsAsync(TaskQuery query, CancellationToken ct = default);

    /// <summary>Approved work with nobody on it yet — the assignment coordinator's queue.</summary>
    Task<PagedResult<TaskSummaryDto>> AssignmentQueueAsync(PageQuery page, CancellationToken ct = default);

    /// <summary>One person's ordered work queue.</summary>
    Task<IReadOnlyList<TaskSummaryDto>> MyQueueAsync(long userId, CancellationToken ct = default);

    /// <summary>Per-assignee load, for capacity decisions.</summary>
    Task<IReadOnlyList<WorkloadDto>> WorkloadAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PauseReasonDto>> PauseReasonsAsync(CancellationToken ct = default);

    /// <summary>
    /// People work can actually be given to: active accounts holding <c>Task.Work</c>. A coordinator
    /// needs this to fill the assign dialog, and it deliberately does not require the full
    /// user-administration permission — a name and an id is all it exposes.
    /// </summary>
    Task<IReadOnlyList<AssignableUserDto>> AssignableUsersAsync(CancellationToken ct = default);

    /// <summary>
    /// Who this particular task could go to, with the facts a coordinator decides on
    /// (PRODUCT-CORE §12C). Task-specific, because "have they worked on this part of the product
    /// before" is half the question and cannot be answered without knowing what is being assigned.
    /// </summary>
    Task<Result<IReadOnlyList<AssignmentCandidateDto>>> AssignmentCandidatesAsync(
        long taskId, CancellationToken ct = default);
}

public sealed class TaskQueryService : ITaskQueryService
{
    /// <summary>Statuses that mean the task is finished with, one way or another.</summary>
    private static readonly WorkTaskStatus[] TerminalStatuses =
    {
        WorkTaskStatus.Closed, WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate
    };

    private readonly IWorkflowDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ITaskDependencyService _dependencies;
    private readonly IBusinessCalendar _calendar;
    private readonly IDateTimeProvider _clock;

    public TaskQueryService(
        IWorkflowDbContext db, ICurrentUser currentUser, ITaskDependencyService dependencies,
        IBusinessCalendar calendar, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _dependencies = dependencies;
        _calendar = calendar;
        _clock = clock;
    }

    public async Task<Result<TaskDetailDto>> GetAsync(long taskId, CancellationToken ct = default)
    {
        var task = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (!await CanSeeAsync(task, ct))
        {
            // Not Found rather than Forbidden, deliberately. "You may not see this" still confirms
            // the task exists, which is most of what an id-guessing probe wants to learn. The list
            // was scoped when item 2 landed; this is the same rule on the way in through a URL.
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));
        }

        var assigneeName = task.PrimaryAssigneeUserId is { } assigneeId
            ? await _db.Users.AsNoTracking().Where(u => u.Id == assigneeId)
                .Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        // What was asked for, carried onto the work. This is what stops a worker having to go and
        // read the request to find the screenshot or what "working" is supposed to look like.
        var requestContext = task.RequestId is { } requestId
            ? await _db.Requests.AsNoTracking()
                .Where(r => r.Id == requestId)
                .Select(r => new RequestContextDto(
                    r.Id,
                    r.RequestNumber,
                    r.RequestedByUser.DisplayName,
                    r.RequestedAt,
                    r.RequestedUrgency,
                    r.ProjectId == null
                        ? null
                        : _db.Projects.Where(x => x.Id == r.ProjectId).Select(x => x.Name).FirstOrDefault(),
                    r.ModuleId == null
                        ? null
                        : _db.Modules.Where(x => x.Id == r.ModuleId).Select(x => x.Name).FirstOrDefault(),
                    r.Description,
                    r.BusinessImpact,
                    r.ExpectedResult,
                    r.CurrentResult,
                    r.ReproductionSteps,
                    // The batch's own files come across too: a screenshot showing all eight
                    // problems belongs to the submission, and a worker looking at item three needs
                    // it as much as a worker looking at item one.
                    _db.Attachments
                        .Where(a => a.RequestId == r.Id || (r.BatchId != null && a.BatchId == r.BatchId))
                        .OrderBy(a => a.CreatedAt)
                        .Select(a => new AttachmentDto(
                            a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes,
                            a.UploadedByUserId, a.CreatedAt))
                        .ToList(),
                    r.BatchId,
                    r.Batch != null ? r.Batch.BatchNumber : null,
                    // Every *other* request pointing at this task. Reading it from GeneratedTaskId
                    // rather than from the batch is what makes it right: the fold is recorded there,
                    // and a reviewer could fold in two sittings.
                    _db.Requests
                        .Where(o => o.GeneratedTaskId == taskId && o.Id != r.Id)
                        .OrderBy(o => o.OrdinalInBatch).ThenBy(o => o.Id)
                        .Select(o => new FoldedRequestDto(
                            o.Id, o.RequestNumber, o.Title, o.Description,
                            o.RequestedByUser.DisplayName))
                        .ToList()))
                .FirstOrDefaultAsync(ct)
            : null;

        var requestNumber = requestContext?.RequestNumber;

        var pauseReasons = await _db.PauseReasons.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var sessions = await _db.WorkSessions.AsNoTracking()
            .Where(s => s.TaskId == taskId)
            .OrderBy(s => s.SessionStart)
            .ToListAsync(ct);

        // Names for every actor in the three history streams, resolved in one query. A timeline of
        // user ids is a timeline nobody can read, and one lookup per row would be a page-load's
        // worth of round trips on a task with any history at all.
        var actorIds = await _db.StatusHistories.AsNoTracking()
            .Where(h => h.TaskId == taskId).Select(h => h.ChangedByUserId)
            .Concat(_db.AssignmentHistories.AsNoTracking()
                .Where(h => h.TaskId == taskId).Select(h => h.AssignedByUserId))
            .Concat(_db.AssignmentHistories.AsNoTracking()
                .Where(h => h.TaskId == taskId && h.FromUserId != null).Select(h => h.FromUserId!.Value))
            .Concat(_db.AssignmentHistories.AsNoTracking()
                .Where(h => h.TaskId == taskId && h.ToUserId != null).Select(h => h.ToUserId!.Value))
            .Concat(_db.TaskActivities.AsNoTracking()
                .Where(a => a.TaskId == taskId).Select(a => a.ActorUserId))
            .Distinct()
            .ToListAsync(ct);

        var actorNames = await _db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        string? NameOf(long? id) =>
            id is { } value && actorNames.TryGetValue(value, out var name) ? name : null;

        var statusHistory = (await _db.StatusHistories.AsNoTracking()
                .Where(h => h.TaskId == taskId)
                .OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
                .ToListAsync(ct))
            .Select(h => new StatusHistoryDto(
                h.Id, h.FromStatus, h.ToStatus, h.ChangedByUserId, NameOf(h.ChangedByUserId),
                h.ChangedAt, h.Reason, h.WasOverride))
            .ToList();

        var assignmentHistory = (await _db.AssignmentHistories.AsNoTracking()
                .Where(h => h.TaskId == taskId)
                .OrderBy(h => h.AssignedAt).ThenBy(h => h.Id)
                .ToListAsync(ct))
            .Select(h => new AssignmentHistoryDto(
                h.Id, h.FromUserId, NameOf(h.FromUserId), h.ToUserId, NameOf(h.ToUserId),
                h.AssignedByUserId, NameOf(h.AssignedByUserId), h.AssignedAt, h.Reason))
            .ToList();

        var activity = (await _db.TaskActivities.AsNoTracking()
                .Where(a => a.TaskId == taskId)
                .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
                .ToListAsync(ct))
            .Select(a => new TaskActivityDto(
                a.Id, a.Type, a.ActorUserId, NameOf(a.ActorUserId), a.OccurredAt, a.Description))
            .ToList();

        var qcReviews = await _db.QCReviews.AsNoTracking()
            .Where(q => q.TaskId == taskId)
            .OrderBy(q => q.AttemptNumber)
            .ToListAsync(ct);

        var reviewerNames = await _db.Users.AsNoTracking()
            .Where(u => qcReviews.Select(q => q.ReviewerUserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        // Every attempt's evidence in one query, keyed by attempt.
        var qcReviewIds = qcReviews.Select(q => q.Id).ToList();
        var qcEvidence = (await _db.Attachments.AsNoTracking()
                .Where(a => a.QCReviewId != null && qcReviewIds.Contains(a.QCReviewId!.Value))
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct))
            .GroupBy(a => a.QCReviewId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AttachmentDto>)g.Select(a => new AttachmentDto(
                    a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes,
                    a.UploadedByUserId, a.CreatedAt)).ToList());

        // Required first, so what actually blocks the parent is at the top of the list.
        var subTasks = await _db.Tasks.AsNoTracking()
            .Where(t => t.ParentTaskId == taskId)
            .OrderByDescending(t => t.IsRequired).ThenBy(t => t.Id)
            .Select(t => new SubtaskSummaryDto(
                t.Id, t.TaskNumber, t.Title, t.Status,
                t.PrimaryAssigneeUser != null ? t.PrimaryAssigneeUser.DisplayName : null,
                t.ProgressPercent, t.IsRequired))
            .ToListAsync(ct);

        var blockedBy = await _dependencies.BlockersAsync(taskId, ct);

        // The task's own files, read once and split by what they are for. QC evidence is left out
        // deliberately: it belongs to a numbered attempt and is returned with that attempt, not
        // loose on the task where it would lose the one thing that makes it meaningful.
        var taskFiles = await _db.Attachments.AsNoTracking()
            .Where(a => a.TaskId == taskId && a.Kind != AttachmentKind.QCEvidence)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes,
                a.UploadedByUserId, a.CreatedAt, a.Kind })
            .ToListAsync(ct);

        IReadOnlyList<AttachmentDto> FilesOfKind(AttachmentKind kind) => taskFiles
            .Where(a => a.Kind == kind)
            .Select(a => new AttachmentDto(
                a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes, a.UploadedByUserId, a.CreatedAt))
            .ToList();

        // Names, not bare ids: a support person nobody can see by name is not "visible on the task".
        // Ordered before projecting, and joined through the navigation rather than an explicit
        // Join: ordering by a member of the constructed DTO is not translatable to SQL.
        var supportPeople = await _db.TaskCollaborators.AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.AddedAt)
            .Select(c => new SupportPersonDto(
                c.UserId, c.User.DisplayName, c.AddedAt, c.AddedByUserId))
            .ToListAsync(ct);

        var transitions = TaskWorkflow.Transitions
            .Where(t => t.From == task.Status && _currentUser.Permissions.Contains(t.RequiredPermission))
            .Select(t => t.To)
            .ToList();

        if (_currentUser.Permissions.Contains(Permissions.TaskCancel) &&
            TaskWorkflow.IsAllowed(task.Status, WorkTaskStatus.Cancelled))
        {
            transitions.Add(WorkTaskStatus.Cancelled);
        }

        // Looked up individually and null-safe: a client retired after the task was raised must
        // not make the task unreadable. Each is skipped entirely when the id is null, so an
        // internal task with no client costs nothing.
        var clientName = task.ClientId is { } clientId
            ? await _db.Clients.AsNoTracking().Where(c => c.Id == clientId)
                .Select(c => c.Name).FirstOrDefaultAsync(ct)
            : null;

        return Result<TaskDetailDto>.Success(ScopeToAudience(new TaskDetailDto(
            task.Id, task.TaskNumber, task.RequestId, requestNumber, task.Title, task.Description,
            task.Type, task.Status, task.Priority, task.ClientId, clientName,
            task.PrimaryAssigneeUserId, assigneeName, task.ReviewerUserId, task.QCUserId,
            task.EstimatedEffortHours, task.DueDate, task.AcceptanceCriteria, task.Resolution,
            task.ProgressPercent, task.QueueOrder, task.ParentTaskId,
            transitions.Distinct().ToList(),
            TotalWorked(sessions),
            supportPeople,
            sessions.Select(s => ToDto(s, pauseReasons)).ToList(),
            statusHistory, assignmentHistory, activity,
            qcReviews.Select(q => new QCReviewDto(
                q.Id, q.TaskId, q.AttemptNumber, q.ReviewerUserId,
                reviewerNames.TryGetValue(q.ReviewerUserId, out var reviewerName) ? reviewerName : null,
                q.ReviewedAt, q.Result, q.Comments, q.Environment, q.BuildVersion,
                AcceptanceCriteria.Deserialize(q.AcceptanceCriteriaResults),
                qcEvidence.TryGetValue(q.Id, out var files) ? files : Array.Empty<AttachmentDto>())).ToList(),
            subTasks,
            blockedBy,
            EncodeRowVersion(task.RowVersion),
            requestContext,
            FilesOfKind(AttachmentKind.CompletionProof),
            FilesOfKind(AttachmentKind.General))));
    }

    // --- who sees what ---------------------------------------------------------------------

    /// <summary>
    /// Permissions that make somebody's job span the whole floor. Anyone holding one of these reads
    /// any task, because a coordinator who could only see their own work could not coordinate.
    /// </summary>
    private static readonly string[] FloorWidePermissions =
    {
        Permissions.TaskAssign, Permissions.TaskReview, Permissions.TaskApprove,
        Permissions.TaskQCReview, Permissions.TaskClose, Permissions.RequestViewAll,
        Permissions.DashboardManagement, Permissions.ReportsView, Permissions.WorkforceViewAll,
    };

    /// <summary>
    /// Everyone else sees only work they are part of: theirs to do, theirs to help with, or grown
    /// from a request they raised. The same three clauses as <c>TaskQuery.VisibleToUserId</c> —
    /// scoping the list and leaving the detail open would have been a lock on the door of an
    /// unwalled room.
    /// </summary>
    private async Task<bool> CanSeeAsync(WorkTask task, CancellationToken ct)
    {
        // No ambient user means an internal caller — a background service or a test harness — not
        // an anonymous request: every HTTP route into this is behind [Authorize].
        if (_currentUser.UserId is not { } viewerId)
            return true;

        if (FloorWidePermissions.Any(_currentUser.Permissions.Contains))
            return true;

        if (task.PrimaryAssigneeUserId == viewerId)
            return true;

        if (await _db.TaskCollaborators.AsNoTracking()
                .AnyAsync(c => c.TaskId == task.Id && c.UserId == viewerId, ct))
            return true;

        return task.RequestId is { } requestId
               && await _db.Requests.AsNoTracking()
                   .AnyAsync(r => r.Id == requestId && r.RequestedByUserId == viewerId, ct);
    }

    /// <summary>
    /// How much of the record the caller is handed, by audience.
    ///
    /// Hiding a panel in the client is presentation; this is the same decision enforced where it
    /// counts. A requester who follows their own work through to the task gets what it is and how
    /// far along it is — not how many times it was reassigned, how long each sitting took, or what
    /// the checker wrote about a colleague's work. None of that is secret exactly; all of it
    /// invites a conversation the requester is not equipped to have, and the estimate in
    /// particular gets read as a promise.
    ///
    /// Workers and coordinators are handed the record whole: they are inside the process.
    /// </summary>
    private TaskDetailDto ScopeToAudience(TaskDetailDto dto)
    {
        if (StatusViews.AudienceFor(_currentUser.Permissions) != StatusAudience.Requester)
            return dto;

        return dto with
        {
            EstimatedEffortHours = null,
            QueueOrder = 0,
            TotalWorkedTime = TimeSpan.Zero,
            WorkSessions = Array.Empty<WorkSessionDto>(),
            AssignmentHistory = Array.Empty<AssignmentHistoryDto>(),
            StatusHistory = Array.Empty<StatusHistoryDto>(),
            Activity = Array.Empty<TaskActivityDto>(),
            QCReviews = Array.Empty<QCReviewDto>(),
        };
    }

    public async Task<PagedResult<TaskSummaryDto>> ListAsync(
        TaskQuery query, PageQuery page, CancellationToken ct = default)
    {
        // The filter row is applied here and nowhere else — never in StatusCountsAsync, so the
        // tiles do not move as someone types into a column. Same rule as the request grid.
        var tasks = ApplyColumnFilters(
            ApplyFilters(_db.Tasks.AsNoTracking(), query, includeStatus: true), query.Columns);

        // Newest first by default. This is the browsing view — filters and tiles are how you narrow
        // it, so recency is the useful default. The working queues (my queue, assignment, QC) keep
        // their own deliberate ordering, where priority and queue position are the point.
        return await ProjectPageAsync(Sort(tasks, query), page, ct);
    }

    /// <summary>
    /// The list's filters, in one place, so the status tiles and the rows beneath them can never
    /// disagree. `includeStatus` is false when counting — a tile has to count across everything the
    /// other filters allow, not within the status already chosen.
    /// </summary>
    /// <summary>
    /// Ordering, driven from the column header the user clicked.
    ///
    /// Sorting happens in the database, not on the page: ordering only the twenty-five rows already
    /// fetched would reorder the page rather than the list, which looks the same until the data
    /// spans more than one page and then quietly lies.
    /// </summary>
    private static IQueryable<WorkTask> Sort(IQueryable<WorkTask> tasks, TaskQuery query)
    {
        var descending = query.SortDescending;

        return query.SortBy?.ToLowerInvariant() switch
        {
            "number" => descending ? tasks.OrderByDescending(t => t.TaskNumber) : tasks.OrderBy(t => t.TaskNumber),
            "title" => descending ? tasks.OrderByDescending(t => t.Title) : tasks.OrderBy(t => t.Title),
            "status" => descending ? tasks.OrderByDescending(t => t.Status) : tasks.OrderBy(t => t.Status),
            "priority" => descending ? tasks.OrderByDescending(t => t.Priority) : tasks.OrderBy(t => t.Priority),
            "client" => descending
                ? tasks.OrderByDescending(t => t.ClientId == null)
                       .ThenByDescending(t => t.ClientId)
                : tasks.OrderBy(t => t.ClientId == null).ThenBy(t => t.ClientId),
            "assignee" => descending
                ? tasks.OrderByDescending(t => t.PrimaryAssigneeUser!.DisplayName)
                : tasks.OrderBy(t => t.PrimaryAssigneeUser!.DisplayName),
            // Nulls last either way: a task with no date is not "the most urgent thing you have".
            "due" => descending
                ? tasks.OrderBy(t => t.DueDate == null).ThenByDescending(t => t.DueDate)
                : tasks.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate),
            _ => descending ? tasks.OrderByDescending(t => t.Id) : tasks.OrderBy(t => t.Id),
        };
    }

    private IQueryable<WorkTask> ApplyFilters(
        IQueryable<WorkTask> tasks, TaskQuery query, bool includeStatus)
    {
        if (query.AssigneeUserId is { } assigneeId)
            tasks = tasks.Where(t => t.PrimaryAssigneeUserId == assigneeId);

        if (query.ParentTaskId is { } parentId)
            tasks = tasks.Where(t => t.ParentTaskId == parentId);

        if (query.Unassigned == true)
            tasks = tasks.Where(t => t.PrimaryAssigneeUserId == null);

        if (includeStatus && query.Status is { } status)
            tasks = tasks.Where(t => t.Status == status);

        if (includeStatus && StatusViews.FindTaskView(query.Audience, query.View) is { } view)
        {
            var statuses = view.Statuses.ToList();
            tasks = tasks.Where(t => statuses.Contains(t.Status));
        }

        if (query.Priority is { } priority)
            tasks = tasks.Where(t => t.Priority == priority);

        if (query.ClientId is { } clientId)
            tasks = tasks.Where(t => t.ClientId == clientId);

        if (query.VisibleToUserId is { } viewerId)
        {
            tasks = tasks.Where(t =>
                t.PrimaryAssigneeUserId == viewerId
                || _db.TaskCollaborators.Any(c => c.TaskId == t.Id && c.UserId == viewerId)
                || (t.RequestId != null && _db.Requests
                        .Any(r => r.Id == t.RequestId && r.RequestedByUserId == viewerId)));
        }

        if (query.OpenOnly)
            tasks = tasks.Where(t => !TerminalStatuses.Contains(t.Status));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            tasks = tasks.Where(t => t.Title.Contains(term) || t.TaskNumber.Contains(term));
        }

        return tasks;
    }

    /// <summary>
    /// The grid's filter row. Keys are the column names the client renders — see
    /// <see cref="ColumnFilters"/> for why this is a dictionary and not a property per column.
    /// </summary>
    private IQueryable<WorkTask> ApplyColumnFilters(IQueryable<WorkTask> tasks, ColumnFilters columns)
    {
        if (!columns.Any) return tasks;

        if (columns.Text("number") is { } number)
            tasks = tasks.Where(t => t.TaskNumber.Contains(number));

        if (columns.Text("title") is { } title)
            tasks = tasks.Where(t => t.Title.Contains(title));

        var clientIds = columns.Ids("client");
        if (clientIds.Count > 0)
            tasks = tasks.Where(t => t.ClientId != null && clientIds.Contains(t.ClientId.Value));

        var priorities = columns.Enums<Priority>("priority");
        if (priorities.Count > 0)
            tasks = tasks.Where(t => priorities.Contains(t.Priority));

        // By name, for the same reason as the request grid: the person list is behind Task.Assign.
        // "-" is the exception and the one a coordinator looks for most — unassigned work — which
        // is why it is a value here rather than a separate switch above the grid.
        if (columns.Text("assignee") is { } assignee)
        {
            tasks = assignee == "-"
                ? tasks.Where(t => t.PrimaryAssigneeUserId == null)
                : tasks.Where(t => t.PrimaryAssigneeUser != null
                    && (t.PrimaryAssigneeUser.DisplayName.Contains(assignee)
                        || t.PrimaryAssigneeUser.UserName.Contains(assignee)));
        }

        var statuses = columns.Enums<WorkTaskStatus>("status");
        if (statuses.Count > 0)
            tasks = tasks.Where(t => statuses.Contains(t.Status));

        // The day boundary comes from the business calendar, not from UTC midnight.
        //
        // Filtering [00:00Z, 24:00Z) matched a task due 25 Aug at 00:00+05:00 — which is 24 Aug
        // 19:00Z — when the user asked for the 24th, while the grid printed "Aug 25" beside it. The
        // filter and the column disagreed about what day it was. `Timestamps are UTC; days are
        // business-local` is the rule the reports already follow; this now follows it too.
        if (columns.Date("due") is { } due)
        {
            var (from, to) = _calendar.DayRange(due);
            tasks = tasks.Where(t => t.DueDate != null && t.DueDate >= from && t.DueDate < to);
        }

        return tasks;
    }

    /// <summary>
    /// What each column's dropdown should still offer, given the other columns.
    ///
    /// One small query per column rather than one big projection: each has to see a *different*
    /// filtered set (its own filter removed), so they cannot share a pass. They are cheap — a
    /// DISTINCT over an already-narrow set — and they run only when the grid reloads.
    /// </summary>
    public async Task<FilterOptionsDto> FilterOptionsAsync(
        TaskQuery query, CancellationToken ct = default)
    {
        var columns = new Dictionary<string, IReadOnlyList<string>>();

        // Everything except the filter row: the row's own columns are removed one at a time below.
        var basis = ApplyFilters(_db.Tasks.AsNoTracking(), query, includeStatus: true);

        IQueryable<WorkTask> Excluding(string key) =>
            ApplyColumnFilters(basis, query.Columns.Without(key));

        columns["client"] = (await Excluding("client")
                .Where(t => t.ClientId != null)
                .Select(t => t.ClientId!.Value)
                .Distinct()
                .ToListAsync(ct))
            .Select(id => id.ToString())
            .ToList();

        columns["priority"] = (await Excluding("priority")
                .Select(t => t.Priority)
                .Distinct()
                .ToListAsync(ct))
            .Select(p => p.ToString())
            .ToList();

        columns["status"] = (await Excluding("status")
                .Select(t => t.Status)
                .Distinct()
                .ToListAsync(ct))
            .Select(st => st.ToString())
            .ToList();

        return new FilterOptionsDto(columns);
    }

    /// <summary>
    /// One count per view for this audience — the tiles are the navigation, so they are always all
    /// present, in a fixed order, whether or not anything is in them. A tile that disappears when
    /// it empties is a tile nobody can learn the position of, and "nothing is waiting for
    /// assignment" is worth saying out loud.
    /// </summary>
    public async Task<IReadOnlyList<StatusCountDto>> StatusCountsAsync(
        TaskQuery query, CancellationToken ct = default)
    {
        var counts = await ApplyFilters(_db.Tasks.AsNoTracking(), query, includeStatus: false)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byStatus = counts.ToDictionary(c => c.Status, c => c.Count);

        return StatusViews.ForTasks(query.Audience)
            .Select(view => new StatusCountDto(
                view.Key,
                view.Label,
                view.Statuses.Sum(s => byStatus.TryGetValue(s, out var n) ? n : 0)))
            .ToList();
    }

    public Task<PagedResult<TaskSummaryDto>> AssignmentQueueAsync(PageQuery page, CancellationToken ct = default)
    {
        var queue = _db.Tasks.AsNoTracking()
            .Where(t => t.Status == WorkTaskStatus.ReadyForAssignment)
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.DueDate ?? DateTimeOffset.MaxValue)
            .ThenBy(t => t.Id);

        return ProjectPageAsync(queue, page, ct);
    }

    public async Task<IReadOnlyList<TaskSummaryDto>> MyQueueAsync(long userId, CancellationToken ct = default)
    {
        var tasks = await _db.Tasks.AsNoTracking()
            .Where(t => t.PrimaryAssigneeUserId == userId && !TerminalStatuses.Contains(t.Status))
            // The assignee's own ordering wins; priority only breaks ties.
            .OrderBy(t => t.QueueOrder)
            .ThenBy(t => t.Priority)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);

        return await ProjectAsync(tasks, ct);
    }

    public async Task<IReadOnlyList<WorkloadDto>> WorkloadAsync(CancellationToken ct = default)
    {
        var open = await _db.Tasks.AsNoTracking()
            .Where(t => t.PrimaryAssigneeUserId != null && !TerminalStatuses.Contains(t.Status))
            .Select(t => new
            {
                UserId = t.PrimaryAssigneeUserId!.Value,
                t.Status,
                t.EstimatedEffortHours
            })
            .ToListAsync(ct);

        var userIds = open.Select(t => t.UserId).Distinct().ToList();

        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.WorkforceState })
            .ToListAsync(ct);

        var activeSessions = await _db.WorkSessions.AsNoTracking()
            .Where(s => s.Status == WorkSessionStatus.Active && userIds.Contains(s.UserId))
            .Join(_db.Tasks.AsNoTracking(), s => s.TaskId, t => t.Id,
                (s, t) => new { s.UserId, TaskId = t.Id, t.TaskNumber })
            .ToListAsync(ct);

        var activeByUser = activeSessions.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.First());

        return users
            .Select(u =>
            {
                var theirs = open.Where(t => t.UserId == u.Id).ToList();
                activeByUser.TryGetValue(u.Id, out var active);

                return new WorkloadDto(
                    u.Id,
                    u.DisplayName,
                    u.WorkforceState,
                    theirs.Count,
                    theirs.Count(t => t.Status == WorkTaskStatus.InProgress),
                    theirs.Count(t => t.Status == WorkTaskStatus.Blocked),
                    theirs.Sum(t => t.EstimatedEffortHours ?? 0m),
                    active?.TaskId,
                    active?.TaskNumber,
                    WorkforceStateMachine.IsOnShift(u.WorkforceState));
            })
            .OrderByDescending(w => w.OpenTaskCount)
            .ToList();
    }

    public async Task<IReadOnlyList<AssignableUserDto>> AssignableUsersAsync(CancellationToken ct = default)
    {
        var workerRoleIds = await (
            from rp in _db.RolePermissions.AsNoTracking()
            join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where p.Key == Permissions.TaskWork
            select rp.RoleId).Distinct().ToListAsync(ct);

        return await (
            from u in _db.Users.AsNoTracking()
            where u.IsActive && _db.UserRoles.Any(ur => ur.UserId == u.Id && workerRoleIds.Contains(ur.RoleId))
            orderby u.DisplayName
            select new AssignableUserDto(u.Id, u.UserName, u.DisplayName, u.WorkforceState))
            .ToListAsync(ct);
    }

    public async Task<Result<IReadOnlyList<AssignmentCandidateDto>>> AssignmentCandidatesAsync(
        long taskId, CancellationToken ct = default)
    {
        var task = await _db.Tasks.AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => new { t.Id, t.ClientId, t.ModuleId })
            .FirstOrDefaultAsync(ct);

        if (task is null)
            return Result<IReadOnlyList<AssignmentCandidateDto>>.Failure(
                Error.NotFound("task.not_found", "Task not found."));

        // Start from everyone who may hold work, not from everyone who already does — "who is
        // free" is most of what this screen is asked.
        var candidates = await AssignableUsersAsync(ct);
        if (candidates.Count == 0)
            return Result<IReadOnlyList<AssignmentCandidateDto>>.Success(Array.Empty<AssignmentCandidateDto>());

        var userIds = candidates.Select(u => u.Id).ToList();

        var open = await _db.Tasks.AsNoTracking()
            .Where(t => t.PrimaryAssigneeUserId != null
                        && userIds.Contains(t.PrimaryAssigneeUserId!.Value)
                        && !TerminalStatuses.Contains(t.Status))
            .Select(t => new
            {
                UserId = t.PrimaryAssigneeUserId!.Value,
                t.Id,
                t.Status,
                t.DueDate,
                t.ClientId,
                t.ModuleId,
                t.Title,
            })
            .ToListAsync(ct);

        var running = await _db.WorkSessions.AsNoTracking()
            .Where(w => w.Status == WorkSessionStatus.Active && userIds.Contains(w.UserId))
            .Join(_db.Tasks.AsNoTracking(), w => w.TaskId, t => t.Id,
                (w, t) => new { w.UserId, w.SessionStart, TaskId = t.Id, t.TaskNumber, t.Title })
            .ToListAsync(ct);

        var runningByUser = running
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.First());

        // Being on shift is a fact about the person, and the state machine already knows which
        // states count. Reading it off the user row rather than the shift table keeps this one
        // query instead of one per candidate.
        var states = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.WorkforceState, ct);

        // Recently finished work in the same client or module. Closed work is where the useful
        // signal is: "they have seen this form before" is about experience, not current load.
        var relatedTitles = new Dictionary<long, List<string>>();

        if (task.ClientId is not null || task.ModuleId is not null)
        {
            var related = await _db.Tasks.AsNoTracking()
                .Where(t => t.Id != task.Id
                            && t.PrimaryAssigneeUserId != null
                            && userIds.Contains(t.PrimaryAssigneeUserId!.Value)
                            && ((task.ClientId != null && t.ClientId == task.ClientId)
                                || (task.ModuleId != null && t.ModuleId == task.ModuleId)))
                .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt)
                .Select(t => new { UserId = t.PrimaryAssigneeUserId!.Value, t.Title })
                .Take(200)
                .ToListAsync(ct);

            relatedTitles = related
                .GroupBy(r => r.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => r.Title).Distinct().Take(3).ToList());
        }

        // The business day, not UTC midnight — the same rule the grid date filters use.
        var todayEnds = _calendar.DayRange(_calendar.ToBusinessDate(_clock.UtcNow)).EndExclusive;

        var result = candidates
            .Select(u =>
            {
                var theirs = open.Where(t => t.UserId == u.Id).ToList();
                runningByUser.TryGetValue(u.Id, out var active);

                var state = states.TryGetValue(u.Id, out var s) ? s : u.WorkforceState;
                var activeCount = theirs.Count(t => t.Status == WorkTaskStatus.InProgress);

                return new AssignmentCandidateDto(
                    u.Id,
                    u.DisplayName,
                    state,
                    WorkforceStateMachine.IsOnShift(state),
                    active?.TaskId,
                    active?.TaskNumber,
                    active?.Title,
                    active is null ? null : _clock.UtcNow - active.SessionStart,
                    activeCount,
                    theirs.Count - activeCount,
                    theirs.Count(t => t.DueDate != null && t.DueDate <= todayEnds),
                    relatedTitles.TryGetValue(u.Id, out var titles)
                        ? titles
                        : Array.Empty<string>());
            })
            // People who are here and free first: that is the order the question is asked in.
            .OrderByDescending(c => c.IsOnShift)
            .ThenBy(c => c.ActiveCount + c.WaitingCount)
            .ThenBy(c => c.DisplayName)
            .ToList();

        return Result<IReadOnlyList<AssignmentCandidateDto>>.Success(result);
    }

    public async Task<IReadOnlyList<PauseReasonDto>> PauseReasonsAsync(CancellationToken ct = default) =>
        await _db.PauseReasons.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new PauseReasonDto(
                p.Id, p.Name, p.RequiresComment, p.IsBlocker, p.Category, p.AwayState))
            .ToListAsync(ct);

    // --- helpers -------------------------------------------------------------------------

    private async Task<PagedResult<TaskSummaryDto>> ProjectPageAsync(
        IQueryable<WorkTask> query, PageQuery page, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var tasks = await query.Skip(page.Skip).Take(page.NormalizedPageSize).ToListAsync(ct);
        var items = await ProjectAsync(tasks, ct);

        return new PagedResult<TaskSummaryDto>(items, page.NormalizedPage, page.NormalizedPageSize, total);
    }

    /// <summary>
    /// Fills in the names, dates and history a row needs, for a page of tasks, using a fixed
    /// number of queries rather than a few per row.
    ///
    /// The extra history lookups are what let each status view show columns that mean something —
    /// "waiting since", "started", "checked by" — without the reader opening the task. They are
    /// six more round trips for a page of twenty-five, not six per row.
    /// </summary>
    private async Task<IReadOnlyList<TaskSummaryDto>> ProjectAsync(
        IReadOnlyList<WorkTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return Array.Empty<TaskSummaryDto>();

        var taskIds = tasks.Select(t => t.Id).ToList();
        var assigneeIds = tasks.Where(t => t.PrimaryAssigneeUserId.HasValue)
            .Select(t => t.PrimaryAssigneeUserId!.Value).Distinct().ToList();

        var names = await _db.Users.AsNoTracking()
            .Where(u => assigneeIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var sessions = await _db.WorkSessions.AsNoTracking()
            .Where(s => taskIds.Contains(s.TaskId))
            .Select(s => new { s.TaskId, s.SessionStart, s.SessionEnd, s.Status })
            .ToListAsync(ct);

        // "Whose work is this?" is asked constantly in a queue, and an id cannot answer it.
        var clientIds = tasks.Where(t => t.ClientId.HasValue)
            .Select(t => t.ClientId!.Value).Distinct().ToList();

        var clientNames = clientIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.Clients.AsNoTracking()
                .Where(c => clientIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        // How long this has been where it is, and why. Every "waiting since" column comes from
        // here; so does the pause/block reason, which the transition rules already made mandatory.
        var statusMoves = await _db.StatusHistories.AsNoTracking()
            .Where(h => taskIds.Contains(h.TaskId))
            .Select(h => new { h.TaskId, h.ToStatus, h.ChangedAt, h.Reason })
            .ToListAsync(ct);

        var assignments = await _db.AssignmentHistories.AsNoTracking()
            .Where(a => taskIds.Contains(a.TaskId) && a.ToUserId != null)
            .Select(a => new { a.TaskId, a.AssignedAt })
            .ToListAsync(ct);

        var checks = await _db.QCReviews.AsNoTracking()
            .Where(q => taskIds.Contains(q.TaskId))
            .Select(q => new { q.TaskId, q.ReviewerUserId, q.ReviewedAt, q.Comments, q.AttemptNumber })
            .ToListAsync(ct);

        var support = await _db.TaskCollaborators.AsNoTracking()
            .Where(c => taskIds.Contains(c.TaskId))
            .Select(c => new { c.TaskId, c.UserId })
            .ToListAsync(ct);

        var requestIds = tasks.Where(t => t.RequestId.HasValue)
            .Select(t => t.RequestId!.Value).Distinct().ToList();

        var requests = requestIds.Count == 0
            ? new Dictionary<long, RequestOrigin>()
            : await _db.Requests.AsNoTracking()
                .Where(r => requestIds.Contains(r.Id))
                .Select(r => new { r.Id, r.RequestNumber, r.RequestedByUserId, r.ExpectedResult })
                .ToDictionaryAsync(
                    r => r.Id,
                    r => new RequestOrigin(r.RequestNumber, r.RequestedByUserId, r.ExpectedResult),
                    ct);

        // The product area, beside the client. Same reason as the client name: an id in a queue
        // answers nothing, and "which form is this about?" is half of what a worker needs.
        var moduleIds = tasks.Where(t => t.ModuleId.HasValue)
            .Select(t => t.ModuleId!.Value).Distinct().ToList();

        var moduleNames = moduleIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.Modules.AsNoTracking()
                .Where(m => moduleIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        // Is there a picture? Counted across the task and the request it came from, because to the
        // person about to start the work they are one pile. QC evidence is left out on purpose:
        // it belongs to a numbered attempt, and it is not context for starting.
        var attachmentCounts = await _db.Attachments.AsNoTracking()
            .Where(a => (a.TaskId != null && taskIds.Contains(a.TaskId.Value))
                        || (a.RequestId != null && requestIds.Contains(a.RequestId.Value)))
            .Where(a => a.Kind != AttachmentKind.QCEvidence)
            .Select(a => new { a.TaskId, a.RequestId })
            .ToListAsync(ct);

        // One more name lookup, for everyone the rows above referred to but the assignee list did
        // not already cover: requesters, quality checkers and support people.
        var extraIds = requests.Values.Select(r => r.RequestedByUserId)
            .Concat(checks.Select(c => c.ReviewerUserId))
            .Concat(support.Select(c => c.UserId))
            .Concat(tasks.Where(t => t.QCUserId.HasValue).Select(t => t.QCUserId!.Value))
            .Distinct()
            .Where(id => !names.ContainsKey(id))
            .ToList();

        var moreNames = extraIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.Users.AsNoTracking()
                .Where(u => extraIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        string? NameOf(long? userId) =>
            userId is { } id
                ? names.TryGetValue(id, out var known) ? known
                    : moreNames.TryGetValue(id, out var other) ? other
                    : null
                : null;

        return tasks.Select(t =>
        {
            var theirs = sessions.Where(s => s.TaskId == t.Id).ToList();

            var worked = theirs
                .Where(s => s.SessionEnd.HasValue)
                .Aggregate(TimeSpan.Zero, (sum, s) => sum + (s.SessionEnd!.Value - s.SessionStart));

            // The move that put it in the status it is in now.
            var landed = statusMoves
                .Where(h => h.TaskId == t.Id && h.ToStatus == t.Status)
                .OrderByDescending(h => h.ChangedAt)
                .FirstOrDefault();

            var handedToQC = statusMoves
                .Where(h => h.TaskId == t.Id && h.ToStatus == WorkTaskStatus.CompletedReadyForQC)
                .OrderByDescending(h => h.ChangedAt)
                .FirstOrDefault();

            var lastCheck = checks
                .Where(q => q.TaskId == t.Id)
                .OrderByDescending(q => q.AttemptNumber)
                .FirstOrDefault();

            var helpers = support
                .Where(c => c.TaskId == t.Id)
                .Select(c => NameOf(c.UserId))
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList();

            var origin = t.RequestId is { } rid && requests.TryGetValue(rid, out var found)
                ? found
                : null;

            return new TaskSummaryDto(
                t.Id, t.TaskNumber, t.Title, t.Type, t.Status, t.Priority,
                t.PrimaryAssigneeUserId,
                NameOf(t.PrimaryAssigneeUserId),
                t.DueDate, t.QueueOrder, t.ProgressPercent, t.EstimatedEffortHours,
                worked,
                theirs.Any(s => s.Status == WorkSessionStatus.Active),
                t.ClientId,
                t.ClientId is { } cid && clientNames.TryGetValue(cid, out var client) ? client : null,
                // Falls back to the row's own timestamps: a task created straight into the status
                // it is still in has no transition to point at.
                landed?.ChangedAt ?? t.UpdatedAt ?? t.CreatedAt,
                landed?.Reason,
                assignments.Where(a => a.TaskId == t.Id)
                    .OrderByDescending(a => a.AssignedAt)
                    .Select(a => (DateTimeOffset?)a.AssignedAt)
                    .FirstOrDefault(),
                theirs.Count == 0 ? null : theirs.Min(s => s.SessionStart),
                handedToQC?.ChangedAt,
                t.RequestId,
                origin?.RequestNumber,
                origin is null ? null : NameOf(origin.RequestedByUserId),
                lastCheck is null ? null : NameOf(lastCheck.ReviewerUserId),
                lastCheck?.ReviewedAt,
                lastCheck?.Comments,
                NameOf(t.QCUserId),
                helpers,
                t.ModuleId,
                t.ModuleId is { } mid && moduleNames.TryGetValue(mid, out var module) ? module : null,
                origin?.ExpectedResult,
                attachmentCounts.Count(a =>
                    a.TaskId == t.Id || (a.RequestId != null && a.RequestId == t.RequestId)));
        }).ToList();
    }

    /// <summary>Where a task came from, for the rows that show who asked for the work.</summary>
    private sealed record RequestOrigin(
        string RequestNumber, long RequestedByUserId, string? ExpectedResult);

    /// <summary>Elapsed time across all closed sessions. An open session is not counted until it ends.</summary>
    internal static TimeSpan TotalWorked(IEnumerable<WorkSession> sessions) =>
        sessions.Where(s => s.SessionEnd.HasValue)
            .Aggregate(TimeSpan.Zero, (sum, s) => sum + (s.SessionEnd!.Value - s.SessionStart));

    internal static WorkSessionDto ToDto(WorkSession s, IReadOnlyDictionary<long, string> pauseReasons) =>
        new(s.Id, s.TaskId, s.UserId, s.SessionStart, s.SessionEnd,
            s.SessionEnd.HasValue ? s.SessionEnd.Value - s.SessionStart : null,
            s.Status, s.EndPauseReasonId,
            s.EndPauseReasonId is { } reasonId && pauseReasons.TryGetValue(reasonId, out var name) ? name : null,
            s.EndComment, s.EndedByInterruption, s.InterruptedByTaskId);

    /// <summary>
    /// The concurrency token as an opaque string the client can hand back untouched. Null on
    /// providers without ROWVERSION, where the guard simply does not apply.
    /// </summary>
    internal static string? EncodeRowVersion(byte[]? rowVersion) =>
        rowVersion is null or { Length: 0 } ? null : Convert.ToBase64String(rowVersion);
}
