using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Domain.Entities.Workforce;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Workforce.Services;

public interface IShiftMaintenanceService
{
    /// <summary>Closes shifts left open past the configured maximum. Returns how many were closed.</summary>
    Task<int> CloseStaleShiftsAsync(CancellationToken ct = default);
}

/// <summary>
/// Handles the "user closed the browser without ending their shift" case.
///
/// A shift open past <see cref="WorkforceOptions.MaxShiftHours"/> was abandoned, not worked. Left
/// alone it would inflate attendance reports forever and block the user from starting their next
/// shift, because only one shift may be open per user.
///
/// The close is deliberately conservative: the shift ends at the last moment we have evidence the
/// user was present — their final activity event — not at the moment the sweep happened to notice.
/// It is flagged <see cref="ShiftSession.EndedImproperly"/> so reports can tell a real clock-out
/// from a cleaned-up one, and it is never silently corrected: both the activity timeline and the
/// audit log record it.
/// </summary>
public sealed class ShiftMaintenanceService : IShiftMaintenanceService
{
    private readonly IWorkflowDbContext _db;
    private readonly IActivityLogger _activity;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;
    private readonly WorkforceOptions _options;
    private readonly ILogger<ShiftMaintenanceService> _logger;

    public ShiftMaintenanceService(
        IWorkflowDbContext db,
        IActivityLogger activity,
        IAuditService audit,
        IDateTimeProvider clock,
        IOptions<WorkforceOptions> options,
        ILogger<ShiftMaintenanceService> logger)
    {
        _db = db;
        _activity = activity;
        _audit = audit;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> CloseStaleShiftsAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var cutoff = now.AddHours(-_options.MaxShiftHours);

        var stale = await _db.ShiftSessions
            .Where(s => s.ShiftEnd == null && s.ShiftStart < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0) return 0;

        var userIds = stale.Select(s => s.UserId).Distinct().ToList();

        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        // The last activity event per shift, in one query — that is our best evidence of when the
        // user was actually last present.
        var lastEventByShift = await _db.ActivityEvents
            .Where(e => e.ShiftSessionId != null && stale.Select(s => s.Id).Contains(e.ShiftSessionId!.Value))
            .GroupBy(e => e.ShiftSessionId!.Value)
            .Select(g => new { ShiftSessionId = g.Key, LastAt = g.Max(e => e.OccurredAt) })
            .ToDictionaryAsync(x => x.ShiftSessionId, x => x.LastAt, ct);

        foreach (var shift in stale)
        {
            var endedAt = lastEventByShift.TryGetValue(shift.Id, out var lastAt) && lastAt > shift.ShiftStart
                ? lastAt
                : shift.ShiftStart;

            shift.ShiftEnd = endedAt;
            shift.EndedImproperly = true;
            shift.EndNote =
                $"Automatically closed after exceeding {_options.MaxShiftHours}h without an explicit end.";

            // Backdated to endedAt so the timeline stays chronologically coherent.
            _activity.Record(
                shift.UserId,
                ActivityLabels.ShiftClosedAutomatically,
                WorkforceState.ShiftEnded,
                shift.Id,
                note: shift.EndNote,
                occurredAt: endedAt);

            // They are gone, not merely off-shift — a stale shift means the session was abandoned.
            if (users.TryGetValue(shift.UserId, out var user))
                user.WorkforceState = WorkforceState.NotLoggedIn;

            _audit.Record(
                AuditActions.ShiftAutoClosed,
                actorUserId: null,
                entityType: nameof(ShiftSession),
                entityId: shift.Id,
                newValues: new { shift.UserId, shift.ShiftStart, ShiftEnd = endedAt });

            _logger.LogWarning(
                "Auto-closed stale shift {ShiftId} for user {UserId} (started {ShiftStart}, ended {ShiftEnd})",
                shift.Id, shift.UserId, shift.ShiftStart, endedAt);
        }

        await _db.SaveChangesAsync(ct);
        return stale.Count;
    }
}
