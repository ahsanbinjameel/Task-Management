using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Services;

public interface ITaskCommentService
{
    /// <summary>Appends a comment. Comments are never edited or deleted.</summary>
    Task<Result<TaskCommentDto>> AddAsync(
        long taskId, long authorUserId, AddCommentDto request, CancellationToken ct = default);

    /// <summary>The comments this caller is allowed to see, oldest first.</summary>
    Task<Result<IReadOnlyList<TaskCommentDto>>> ListAsync(
        long taskId, long viewerUserId, CancellationToken ct = default);
}

/// <summary>
/// The task conversation. Two things make it more than a list of strings.
///
/// <b>Visibility defaults come from the category, not the caller.</b> An internal or technical note
/// is hidden from the requester unless somebody deliberately says otherwise — the opposite default
/// would mean one forgotten checkbox leaks an internal note to a customer.
///
/// <b>Filtering happens on read, server-side.</b> The requester's own view of a task calls the same
/// endpoint everyone else does; what differs is what comes back.
/// </summary>
public sealed class TaskCommentService : ITaskCommentService
{
    private readonly IWorkflowDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public TaskCommentService(IWorkflowDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>
    /// Whether a category is customer-facing by default. Everything not listed here is internal,
    /// so a category added later is hidden until somebody decides otherwise.
    /// </summary>
    public static bool DefaultRequesterVisibility(CommentCategory category) => category switch
    {
        CommentCategory.RequesterCommunication => true,
        CommentCategory.Clarification => true,
        CommentCategory.ProgressUpdate => true,
        CommentCategory.ResolutionNote => true,
        _ => false
    };

    public async Task<Result<TaskCommentDto>> AddAsync(
        long taskId, long authorUserId, AddCommentDto request, CancellationToken ct = default)
    {
        if (!await _db.Tasks.AnyAsync(t => t.Id == taskId, ct))
            return Result<TaskCommentDto>.Failure(Error.NotFound("task.not_found", "Task not found."));

        if (string.IsNullOrWhiteSpace(request.Body))
            return Result<TaskCommentDto>.Failure(Error.Validation(
                "comment.empty", "A comment needs a body."));

        // A management note is a private channel; someone who cannot read them must not write one.
        if (request.Category == CommentCategory.ManagementNote &&
            !_currentUser.Permissions.Contains(Permissions.DashboardManagement))
        {
            return Result<TaskCommentDto>.Failure(Error.Forbidden(
                "comment.management_only", "Only management may post a management note."));
        }

        var comment = new TaskComment
        {
            TaskId = taskId,
            AuthorUserId = authorUserId,
            Category = request.Category,
            Body = request.Body.Trim(),
            VisibleToRequester = request.VisibleToRequester ?? DefaultRequesterVisibility(request.Category)
        };

        _db.TaskComments.Add(comment);

        _db.TaskActivities.Add(new TaskActivity
        {
            TaskId = taskId,
            Type = ActivityType.CommentAdded,
            ActorUserId = authorUserId,
            OccurredAt = _clock.UtcNow,
            Description = $"Comment added ({comment.Category})."
        });

        await _db.SaveChangesAsync(ct);

        var author = await _db.Users.AsNoTracking()
            .Where(u => u.Id == authorUserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);

        return Result<TaskCommentDto>.Success(ToDto(comment, author));
    }

    public async Task<Result<IReadOnlyList<TaskCommentDto>>> ListAsync(
        long taskId, long viewerUserId, CancellationToken ct = default)
    {
        var task = await _db.Tasks.AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => new { t.Id, t.RequestId })
            .FirstOrDefaultAsync(ct);

        if (task is null)
            return Result<IReadOnlyList<TaskCommentDto>>.Failure(
                Error.NotFound("task.not_found", "Task not found."));

        var comments = await _db.TaskComments.AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);

        var visible = comments.Where(c => CanSee(c, viewerUserId)).ToList();

        // The requester sees only what was written for them, unless they are also internal staff.
        if (await IsExternalRequesterAsync(task.RequestId, viewerUserId, ct))
            visible = visible.Where(c => c.VisibleToRequester).ToList();

        var authorIds = visible.Select(c => c.AuthorUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return Result<IReadOnlyList<TaskCommentDto>>.Success(visible
            .Select(c => ToDto(c, names.TryGetValue(c.AuthorUserId, out var n) ? n : null))
            .ToList());
    }

    // --- helpers -------------------------------------------------------------------------

    private bool CanSee(TaskComment comment, long viewerUserId) =>
        comment.Category != CommentCategory.ManagementNote ||
        comment.AuthorUserId == viewerUserId ||
        _currentUser.Permissions.Contains(Permissions.DashboardManagement);

    /// <summary>
    /// The person who raised the originating request, viewing it without any internal role.
    /// Someone who holds <c>Request.ViewAll</c> is staff and sees the full thread even on their own
    /// request.
    /// </summary>
    private async Task<bool> IsExternalRequesterAsync(long? requestId, long viewerUserId, CancellationToken ct)
    {
        if (requestId is null) return false;
        if (_currentUser.Permissions.Contains(Permissions.RequestViewAll)) return false;

        return await _db.Requests.AsNoTracking()
            .AnyAsync(r => r.Id == requestId && r.RequestedByUserId == viewerUserId, ct);
    }

    private static TaskCommentDto ToDto(TaskComment c, string? authorName) =>
        new(c.Id, c.TaskId, c.AuthorUserId, authorName, c.Category, c.Body,
            c.VisibleToRequester, c.CreatedAt);
}
