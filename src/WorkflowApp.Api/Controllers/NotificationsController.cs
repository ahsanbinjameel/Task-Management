using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Notifications;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// The bell icon. Every route acts on the caller's own notifications — there is no user parameter,
/// so one person's inbox is not addressable by another.
/// </summary>
[Route("api/notifications")]
public sealed class NotificationsController : ApiControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
        => Ok(await _notifications.ListAsync(
            CurrentUserId, unreadOnly, new PageQuery { Page = page, PageSize = pageSize }, ct));

    /// <summary>Just the badge number — cheap enough to poll, though SignalR pushes it anyway.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
        => Ok(new { count = await _notifications.UnreadCountAsync(CurrentUserId, ct) });

    [HttpPost("read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead([FromBody] MarkReadDto dto, CancellationToken ct)
        => FromResult(await _notifications.MarkReadAsync(CurrentUserId, dto.NotificationIds, ct));

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
        => FromResult(await _notifications.MarkAllReadAsync(CurrentUserId, ct));
}

/// <summary>
/// The technical audit trail. Read-only by design — the log is append-only and there is deliberately
/// no route here that edits or removes an entry.
/// </summary>
[Route("api/audit")]
[HasPermission(Permissions.AdminViewAudit)]
public sealed class AuditController : ApiControllerBase
{
    private readonly IAuditQueryService _audit;

    public AuditController(IAuditQueryService audit) => _audit = audit;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] long? entityId,
        [FromQuery] long? actorUserId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = new AuditQuery
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ActorUserId = actorUserId,
            From = from,
            To = to
        };

        return Ok(await _audit.ListAsync(query, new PageQuery { Page = page, PageSize = pageSize }, ct));
    }

    /// <summary>The distinct actions on record, for the filter dropdown.</summary>
    [HttpGet("actions")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Actions(CancellationToken ct)
        => Ok(await _audit.ActionsAsync(ct));
}
