using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;

namespace WorkflowApp.Application.Notifications;

public sealed record AuditLogDto(
    long Id,
    DateTimeOffset CreatedAt,
    long? ActorUserId,
    string? ActorDisplayName,
    string Action,
    string? EntityType,
    long? EntityId,
    string? PreviousValues,
    string? NewValues,
    string? IpAddress,
    string? DeviceInfo);

/// <summary>Filters for the audit stream. All optional; combined with AND.</summary>
public sealed record AuditQuery
{
    public string? Action { get; init; }
    public string? EntityType { get; init; }
    public long? EntityId { get; init; }
    public long? ActorUserId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public interface IAuditQueryService
{
    Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery query, PageQuery page, CancellationToken ct = default);

    /// <summary>The distinct actions present in the log, for populating a filter dropdown.</summary>
    Task<IReadOnlyList<string>> ActionsAsync(CancellationToken ct = default);
}

/// <summary>
/// Read access to the technical audit trail. Read-only by design: the log is append-only, and there
/// is deliberately no method here that edits or deletes a row. An administrator who could quietly
/// remove audit entries would make the whole trail worthless.
/// </summary>
public sealed class AuditQueryService : IAuditQueryService
{
    private readonly IWorkflowDbContext _db;

    public AuditQueryService(IWorkflowDbContext db) => _db = db;

    public async Task<PagedResult<AuditLogDto>> ListAsync(
        AuditQuery query, PageQuery page, CancellationToken ct = default)
    {
        var logs = _db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Action))
            logs = logs.Where(a => a.Action == query.Action);

        if (!string.IsNullOrWhiteSpace(query.EntityType))
            logs = logs.Where(a => a.EntityType == query.EntityType);

        if (query.EntityId is { } entityId)
            logs = logs.Where(a => a.EntityId == entityId);

        if (query.ActorUserId is { } actorId)
            logs = logs.Where(a => a.ActorUserId == actorId);

        if (query.From is { } from)
            logs = logs.Where(a => a.CreatedAt >= from);

        if (query.To is { } to)
            logs = logs.Where(a => a.CreatedAt <= to);

        var total = await logs.CountAsync(ct);

        var rows = await logs
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync(ct);

        var actorIds = rows.Where(a => a.ActorUserId.HasValue).Select(a => a.ActorUserId!.Value).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = rows.Select(a => new AuditLogDto(
            a.Id, a.CreatedAt, a.ActorUserId,
            a.ActorUserId is { } id && names.TryGetValue(id, out var name) ? name : null,
            a.Action, a.EntityType, a.EntityId, a.PreviousValues, a.NewValues,
            a.IpAddress, a.DeviceInfo)).ToList();

        return new PagedResult<AuditLogDto>(items, page.NormalizedPage, page.NormalizedPageSize, total);
    }

    public async Task<IReadOnlyList<string>> ActionsAsync(CancellationToken ct = default) =>
        await _db.AuditLogs.AsNoTracking()
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct);
}
