using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Domain.Entities.Tasks;

namespace WorkflowApp.Application.Notifications;

public sealed record NotificationDto(
    long Id,
    string Title,
    string? Body,
    string? LinkEntityType,
    long? LinkEntityId,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record MarkReadDto
{
    [Required]
    public IReadOnlyList<long> NotificationIds { get; init; } = Array.Empty<long>();
}

public interface INotificationService
{
    /// <summary>
    /// Stages a notification. Not saved — the caller commits it with the change that caused it, so a
    /// rolled-back operation cannot leave a notification claiming it happened.
    /// </summary>
    void Raise(long recipientUserId, string title, string? body = null,
        string? linkEntityType = null, long? linkEntityId = null);

    /// <summary>Same, for several people at once. The actor is skipped: you know what you just did.</summary>
    void RaiseFor(IEnumerable<long?> recipientUserIds, long actingUserId, string title,
        string? body = null, string? linkEntityType = null, long? linkEntityId = null);

    Task<PagedResult<NotificationDto>> ListAsync(
        long userId, bool unreadOnly, PageQuery page, CancellationToken ct = default);

    Task<int> UnreadCountAsync(long userId, CancellationToken ct = default);

    /// <summary>Marks the given notifications read. Ids belonging to someone else are ignored.</summary>
    Task<Result> MarkReadAsync(long userId, IReadOnlyList<long> notificationIds, CancellationToken ct = default);

    Task<Result> MarkAllReadAsync(long userId, CancellationToken ct = default);
}

/// <summary>
/// In-app notifications: the bell icon.
///
/// A notification is a pointer, not a copy. It carries a title and a link, and the client fetches
/// the entity when the user clicks — the same reason real-time payloads are thin. A notification
/// that embedded the task would go stale the moment the task moved on.
/// </summary>
public sealed class NotificationService : INotificationService
{
    /// <summary>Link targets, so the client can route a click without parsing prose.</summary>
    public const string LinkTask = "Task";
    public const string LinkRequest = "Request";

    private readonly IWorkflowDbContext _db;
    private readonly IDateTimeProvider _clock;

    public NotificationService(IWorkflowDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public void Raise(long recipientUserId, string title, string? body = null,
        string? linkEntityType = null, long? linkEntityId = null)
    {
        _db.Notifications.Add(new Notification
        {
            RecipientUserId = recipientUserId,
            Title = title,
            Body = body,
            LinkEntityType = linkEntityType,
            LinkEntityId = linkEntityId,
            IsRead = false
        });
    }

    public void RaiseFor(IEnumerable<long?> recipientUserIds, long actingUserId, string title,
        string? body = null, string? linkEntityType = null, long? linkEntityId = null)
    {
        var recipients = recipientUserIds
            .Where(id => id.HasValue && id.Value != actingUserId)
            .Select(id => id!.Value)
            .Distinct();

        foreach (var recipient in recipients)
            Raise(recipient, title, body, linkEntityType, linkEntityId);
    }

    public async Task<PagedResult<NotificationDto>> ListAsync(
        long userId, bool unreadOnly, PageQuery page, CancellationToken ct = default)
    {
        var query = _db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == userId);

        if (unreadOnly) query = query.Where(n => !n.IsRead);

        var total = await query.CountAsync(ct);

        // Unread first, then newest — the order the bell menu is read in.
        var items = await query
            .OrderBy(n => n.IsRead)
            .ThenByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .Select(n => new NotificationDto(
                n.Id, n.Title, n.Body, n.LinkEntityType, n.LinkEntityId, n.IsRead, n.CreatedAt, n.ReadAt))
            .ToListAsync(ct);

        return new PagedResult<NotificationDto>(items, page.NormalizedPage, page.NormalizedPageSize, total);
    }

    public Task<int> UnreadCountAsync(long userId, CancellationToken ct = default) =>
        _db.Notifications.AsNoTracking().CountAsync(n => n.RecipientUserId == userId && !n.IsRead, ct);

    public async Task<Result> MarkReadAsync(
        long userId, IReadOnlyList<long> notificationIds, CancellationToken ct = default)
    {
        if (notificationIds.Count == 0) return Result.Success();

        // Scoped to the caller: passing somebody else's id marks nothing, rather than erroring and
        // thereby confirming the id exists.
        var mine = await _db.Notifications
            .Where(n => n.RecipientUserId == userId && notificationIds.Contains(n.Id) && !n.IsRead)
            .ToListAsync(ct);

        MarkRead(mine);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> MarkAllReadAsync(long userId, CancellationToken ct = default)
    {
        var unread = await _db.Notifications
            .Where(n => n.RecipientUserId == userId && !n.IsRead)
            .ToListAsync(ct);

        MarkRead(unread);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private void MarkRead(IEnumerable<Notification> notifications)
    {
        var now = _clock.UtcNow;

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }
    }
}
