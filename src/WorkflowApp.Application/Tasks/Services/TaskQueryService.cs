using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Workflow;

namespace WorkflowApp.Application.Tasks.Services;

/// <summary>Filters for listing tasks. All optional.</summary>
public sealed record TaskQuery
{
    public long? AssigneeUserId { get; init; }
    public WorkTaskStatus? Status { get; init; }
    public Priority? Priority { get; init; }
    public bool? Unassigned { get; init; }

    /// <summary>Only the children of this task.</summary>
    public long? ParentTaskId { get; init; }
    public string? Search { get; init; }

    /// <summary>Excludes Closed / Cancelled / Duplicate — the default for working views.</summary>
    public bool OpenOnly { get; init; }
}

public interface ITaskQueryService
{
    Task<Result<TaskDetailDto>> GetAsync(long taskId, CancellationToken ct = default);
    Task<PagedResult<TaskSummaryDto>> ListAsync(TaskQuery query, PageQuery page, CancellationToken ct = default);

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

    public TaskQueryService(
        IWorkflowDbContext db, ICurrentUser currentUser, ITaskDependencyService dependencies)
    {
        _db = db;
        _currentUser = currentUser;
        _dependencies = dependencies;
    }

    public async Task<Result<TaskDetailDto>> GetAsync(long taskId, CancellationToken ct = default)
    {
        var task = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return Result<TaskDetailDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        var assigneeName = task.PrimaryAssigneeUserId is { } assigneeId
            ? await _db.Users.AsNoTracking().Where(u => u.Id == assigneeId)
                .Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        var requestNumber = task.RequestId is { } requestId
            ? await _db.Requests.AsNoTracking().Where(r => r.Id == requestId)
                .Select(r => r.RequestNumber).FirstOrDefaultAsync(ct)
            : null;

        var pauseReasons = await _db.PauseReasons.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var sessions = await _db.WorkSessions.AsNoTracking()
            .Where(s => s.TaskId == taskId)
            .OrderBy(s => s.SessionStart)
            .ToListAsync(ct);

        var statusHistory = await _db.StatusHistories.AsNoTracking()
            .Where(h => h.TaskId == taskId)
            .OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
            .Select(h => new StatusHistoryDto(
                h.Id, h.FromStatus, h.ToStatus, h.ChangedByUserId, h.ChangedAt, h.Reason, h.WasOverride))
            .ToListAsync(ct);

        var assignmentHistory = await _db.AssignmentHistories.AsNoTracking()
            .Where(h => h.TaskId == taskId)
            .OrderBy(h => h.AssignedAt).ThenBy(h => h.Id)
            .Select(h => new AssignmentHistoryDto(
                h.Id, h.FromUserId, h.ToUserId, h.AssignedByUserId, h.AssignedAt, h.Reason))
            .ToListAsync(ct);

        var activity = await _db.TaskActivities.AsNoTracking()
            .Where(a => a.TaskId == taskId)
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
            .Select(a => new TaskActivityDto(a.Id, a.Type, a.ActorUserId, a.OccurredAt, a.Description))
            .ToListAsync(ct);

        var qcReviews = await _db.QCReviews.AsNoTracking()
            .Where(q => q.TaskId == taskId)
            .OrderBy(q => q.AttemptNumber)
            .ToListAsync(ct);

        var reviewerNames = await _db.Users.AsNoTracking()
            .Where(u => qcReviews.Select(q => q.ReviewerUserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var subTaskIds = await _db.Tasks.AsNoTracking()
            .Where(t => t.ParentTaskId == taskId)
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var blockedBy = await _dependencies.BlockersAsync(taskId, ct);

        var collaborators = await _db.TaskCollaborators.AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .Select(c => c.UserId)
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

        return Result<TaskDetailDto>.Success(new TaskDetailDto(
            task.Id, task.TaskNumber, task.RequestId, requestNumber, task.Title, task.Description,
            task.Type, task.Status, task.Priority, task.ProjectId, task.ClientId, task.ModuleId,
            task.PrimaryAssigneeUserId, assigneeName, task.ReviewerUserId, task.QCUserId,
            task.EstimatedEffortHours, task.DueDate, task.AcceptanceCriteria, task.Resolution,
            task.ProgressPercent, task.QueueOrder, task.ParentTaskId,
            transitions.Distinct().ToList(),
            TotalWorked(sessions),
            collaborators,
            sessions.Select(s => ToDto(s, pauseReasons)).ToList(),
            statusHistory, assignmentHistory, activity,
            qcReviews.Select(q => new QCReviewDto(
                q.Id, q.TaskId, q.AttemptNumber, q.ReviewerUserId,
                reviewerNames.TryGetValue(q.ReviewerUserId, out var reviewerName) ? reviewerName : null,
                q.ReviewedAt, q.Result, q.Comments, q.Environment, q.BuildVersion,
                AcceptanceCriteria.Deserialize(q.AcceptanceCriteriaResults))).ToList(),
            subTaskIds,
            blockedBy,
            EncodeRowVersion(task.RowVersion)));
    }

    public async Task<PagedResult<TaskSummaryDto>> ListAsync(
        TaskQuery query, PageQuery page, CancellationToken ct = default)
    {
        var tasks = _db.Tasks.AsNoTracking();

        if (query.AssigneeUserId is { } assigneeId)
            tasks = tasks.Where(t => t.PrimaryAssigneeUserId == assigneeId);

        if (query.ParentTaskId is { } parentId)
            tasks = tasks.Where(t => t.ParentTaskId == parentId);

        if (query.Unassigned == true)
            tasks = tasks.Where(t => t.PrimaryAssigneeUserId == null);

        if (query.Status is { } status)
            tasks = tasks.Where(t => t.Status == status);

        if (query.Priority is { } priority)
            tasks = tasks.Where(t => t.Priority == priority);

        if (query.OpenOnly)
            tasks = tasks.Where(t => !TerminalStatuses.Contains(t.Status));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            tasks = tasks.Where(t => t.Title.Contains(term) || t.TaskNumber.Contains(term));
        }

        // Highest priority first, then nearest due date, then oldest.
        return await ProjectPageAsync(
            tasks.OrderBy(t => t.Priority).ThenBy(t => t.DueDate ?? DateTimeOffset.MaxValue).ThenBy(t => t.Id),
            page, ct);
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
                    active?.TaskNumber);
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

    public async Task<IReadOnlyList<PauseReasonDto>> PauseReasonsAsync(CancellationToken ct = default) =>
        await _db.PauseReasons.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new PauseReasonDto(p.Id, p.Name, p.RequiresComment, p.IsBlocker))
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
    /// Fills in assignee names and worked time for a page of tasks using two queries, rather than
    /// two per row.
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

        return tasks.Select(t =>
        {
            var theirs = sessions.Where(s => s.TaskId == t.Id).ToList();

            var worked = theirs
                .Where(s => s.SessionEnd.HasValue)
                .Aggregate(TimeSpan.Zero, (sum, s) => sum + (s.SessionEnd!.Value - s.SessionStart));

            return new TaskSummaryDto(
                t.Id, t.TaskNumber, t.Title, t.Type, t.Status, t.Priority,
                t.PrimaryAssigneeUserId,
                t.PrimaryAssigneeUserId is { } id && names.TryGetValue(id, out var name) ? name : null,
                t.DueDate, t.QueueOrder, t.ProgressPercent, t.EstimatedEffortHours,
                worked,
                theirs.Any(s => s.Status == WorkSessionStatus.Active));
        }).ToList();
    }

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
