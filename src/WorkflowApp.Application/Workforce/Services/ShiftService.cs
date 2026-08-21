using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Services;
using WorkflowApp.Application.Workforce.Dtos;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Workforce;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Workflow;

namespace WorkflowApp.Application.Workforce.Services;

public interface IShiftService
{
    Task<Result<WorkforceStatusDto>> StartShiftAsync(long userId, CancellationToken ct = default);
    Task<Result<WorkforceStatusDto>> EndShiftAsync(long userId, string? note, CancellationToken ct = default);
    Task<Result<WorkforceStatusDto>> ChangeStateAsync(long userId, WorkforceState target, string? note, CancellationToken ct = default);
    Task<Result<WorkforceStatusDto>> GetStatusAsync(long userId, CancellationToken ct = default);

    /// <summary>Supervisor action: close a shift the employee left open. Reason is mandatory.</summary>
    Task<Result<ShiftSessionDto>> ForceEndShiftAsync(long userId, long actingUserId, string reason, CancellationToken ct = default);
}

/// <summary>
/// Owns the shift aggregate: opening and closing shifts, and moving a user through the workforce
/// state machine.
///
/// The rule that shapes this class: a shift session is not an auth session and not a task work
/// session. Logging in does not open a shift, logging out does not close one, and a shift cannot be
/// closed while a work session is still running — that last check reaches outside this aggregate,
/// which is exactly why it lives here rather than in the state machine.
/// </summary>
public sealed class ShiftService : IShiftService
{
    private readonly IWorkflowDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly IActivityLogger _activity;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ShiftService> _logger;

    public ShiftService(
        IWorkflowDbContext db,
        IPermissionService permissions,
        IActivityLogger activity,
        IAuditService audit,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<ShiftService> logger)
    {
        _db = db;
        _permissions = permissions;
        _activity = activity;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<WorkforceStatusDto>> StartShiftAsync(long userId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<WorkforceStatusDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        // Shifts are for people who execute tasks. Reviewers, coordinators, requesters and
        // management use the system without being on the clock, so they never open one.
        if (!await IsShiftTrackedAsync(userId, ct))
            return Result<WorkforceStatusDto>.Failure(Error.Forbidden(
                "shift.not_tracked",
                "Shifts are only tracked for people who work on tasks."));

        // Backed by UX_ShiftSession_OneOpenPerUser; checked here to return a usable message
        // instead of a unique-constraint violation.
        var open = await FindOpenShiftAsync(userId, ct);
        if (open is not null)
            return Result<WorkforceStatusDto>.Failure(
                Error.Conflict("shift.already_open", "A shift is already open. End it before starting another."));

        if (!WorkforceStateMachine.IsAllowed(user.WorkforceState, WorkforceState.Available))
            return Result<WorkforceStatusDto>.Failure(Error.Conflict(
                "workforce.transition_not_allowed",
                $"Cannot start a shift from state {user.WorkforceState}."));

        var transition = WorkforceStateMachine.Find(user.WorkforceState, WorkforceState.Available)!;

        var shift = new ShiftSession
        {
            UserId = userId,
            ShiftStart = now,
            StartDeviceInfo = _currentUser.UserAgent,
            StartIpAddress = _currentUser.IpAddress
        };

        _db.ShiftSessions.Add(shift);
        await _db.SaveChangesAsync(ct);   // need the shift id before the event can reference it

        user.WorkforceState = WorkforceState.Available;
        _activity.Record(userId, transition.Label, WorkforceState.Available, shift.Id, occurredAt: now);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Shift {ShiftId} started for user {UserId}", shift.Id, userId);
        return Result<WorkforceStatusDto>.Success(await BuildStatusAsync(user, shift, ct));
    }

    public async Task<Result<WorkforceStatusDto>> EndShiftAsync(long userId, string? note, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<WorkforceStatusDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        var shift = await FindOpenShiftAsync(userId, ct);
        if (shift is null)
            return Result<WorkforceStatusDto>.Failure(
                Error.Conflict("shift.not_open", "There is no open shift to end."));

        // Ending a shift with a task still running would orphan the work session and lose the
        // time. Make the user stop the task first, deliberately.
        if (await HasActiveWorkSessionAsync(userId, ct))
            return Result<WorkforceStatusDto>.Failure(Error.Conflict(
                "shift.work_session_active",
                "Stop or pause the task you are working on before ending your shift."));

        if (!WorkforceStateMachine.IsAllowed(user.WorkforceState, WorkforceState.ShiftEnded))
            return Result<WorkforceStatusDto>.Failure(Error.Conflict(
                "workforce.transition_not_allowed",
                $"Cannot end a shift from state {user.WorkforceState}."));

        var transition = WorkforceStateMachine.Find(user.WorkforceState, WorkforceState.ShiftEnded)!;

        shift.ShiftEnd = now;
        shift.EndNote = note;
        user.WorkforceState = WorkforceState.ShiftEnded;

        _activity.Record(userId, transition.Label, WorkforceState.ShiftEnded, shift.Id, note: note, occurredAt: now);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Shift {ShiftId} ended for user {UserId}", shift.Id, userId);
        return Result<WorkforceStatusDto>.Success(await BuildStatusAsync(user, shift, ct));
    }

    public async Task<Result<WorkforceStatusDto>> ChangeStateAsync(
        long userId, WorkforceState target, string? note, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<WorkforceStatusDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        // Working and ShiftEnded are reachable states, but not by asking for them: Working is
        // entered by starting a task, ShiftEnded by ending the shift. Allowing either here would
        // let availability claim work that is not happening.
        if (!WorkforceStateMachine.IsSelfServiceTarget(target))
            return Result<WorkforceStatusDto>.Failure(Error.Validation(
                "workforce.state_not_self_service",
                target == WorkforceState.Working
                    ? "Working is entered by starting a task, not by setting your state."
                    : $"{target} cannot be set directly."));

        var shift = await FindOpenShiftAsync(userId, ct);
        if (shift is null)
            return Result<WorkforceStatusDto>.Failure(
                Error.Conflict("shift.not_open", "Start your shift before changing your availability."));

        if (user.WorkforceState == target)
            return Result<WorkforceStatusDto>.Success(await BuildStatusAsync(user, shift, ct));

        if (!WorkforceStateMachine.IsAllowed(user.WorkforceState, target))
            return Result<WorkforceStatusDto>.Failure(Error.Conflict(
                "workforce.transition_not_allowed",
                $"Cannot move from {user.WorkforceState} to {target}."));

        var transition = WorkforceStateMachine.Find(user.WorkforceState, target)!;

        user.WorkforceState = target;
        _activity.Record(userId, transition.Label, target, shift.Id, note: note, occurredAt: now);

        await _db.SaveChangesAsync(ct);
        return Result<WorkforceStatusDto>.Success(await BuildStatusAsync(user, shift, ct));
    }

    public async Task<Result<WorkforceStatusDto>> GetStatusAsync(long userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<WorkforceStatusDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        var shift = await _db.ShiftSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.ShiftEnd == null)
            .FirstOrDefaultAsync(ct);

        return Result<WorkforceStatusDto>.Success(await BuildStatusAsync(user, shift, ct));
    }

    public async Task<Result<ShiftSessionDto>> ForceEndShiftAsync(
        long userId, long actingUserId, string reason, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        if (string.IsNullOrWhiteSpace(reason))
            return Result<ShiftSessionDto>.Failure(
                Error.Validation("shift.reason_required", "A reason is required to end someone else's shift."));

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<ShiftSessionDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        var shift = await FindOpenShiftAsync(userId, ct);
        if (shift is null)
            return Result<ShiftSessionDto>.Failure(
                Error.Conflict("shift.not_open", "That user has no open shift."));

        shift.ShiftEnd = now;
        shift.EndNote = reason;
        shift.EndedByUserId = actingUserId;
        // A supervisor closing it means the employee did not close it themselves.
        shift.EndedImproperly = true;

        user.WorkforceState = WorkforceState.ShiftEnded;

        _activity.Record(
            userId, ActivityLabels.ShiftForceEnded, WorkforceState.ShiftEnded, shift.Id,
            note: reason, occurredAt: now);

        _audit.Record(
            AuditActions.ShiftForceEnded,
            actorUserId: actingUserId,
            entityType: nameof(ShiftSession),
            entityId: shift.Id,
            newValues: new { TargetUserId = userId, Reason = reason });

        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Shift {ShiftId} for user {UserId} force-ended by user {ActingUserId}", shift.Id, userId, actingUserId);

        return Result<ShiftSessionDto>.Success(ToDto(shift, user.DisplayName));
    }

    // --- helpers -------------------------------------------------------------------------

    /// <summary>
    /// Whether this user is on the clock. Read from the database rather than the caller's token,
    /// because a supervisor may be acting on someone else's record.
    /// </summary>
    private Task<bool> IsShiftTrackedAsync(long userId, CancellationToken ct) =>
        _permissions.HasPermissionAsync(userId, Permissions.WorkforceTrackShift, ct);

    private Task<ShiftSession?> FindOpenShiftAsync(long userId, CancellationToken ct) =>
        _db.ShiftSessions.FirstOrDefaultAsync(s => s.UserId == userId && s.ShiftEnd == null, ct);

    /// <summary>
    /// Phase 6 owns work sessions, but the entity already exists and the "no orphaned session"
    /// rule matters from the moment shifts can be closed.
    /// </summary>
    private Task<bool> HasActiveWorkSessionAsync(long userId, CancellationToken ct) =>
        _db.WorkSessions.AnyAsync(s => s.UserId == userId && s.Status == WorkSessionStatus.Active, ct);

    private async Task<WorkforceStatusDto> BuildStatusAsync(User user, ShiftSession? shift, CancellationToken ct)
    {
        // When the state last changed — the newest event that actually carried a state.
        var stateSince = await _db.ActivityEvents.AsNoTracking()
            .Where(e => e.UserId == user.Id && e.ResultingState == user.WorkforceState)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Select(e => (DateTimeOffset?)e.OccurredAt)
            .FirstOrDefaultAsync(ct);

        var isTracked = await IsShiftTrackedAsync(user.Id, ct);

        return new WorkforceStatusDto(
            user.Id,
            user.DisplayName,
            user.WorkforceState,
            DailyTimelineBuilder.Humanize(user.WorkforceState),
            WorkforceStateMachine.IsOnShift(user.WorkforceState),
            isTracked,
            stateSince,
            shift is null ? null : ToDto(shift, user.DisplayName),
            // Nothing to offer someone who is not on the clock.
            isTracked
                ? WorkforceStateMachine.SelfServiceNextStates(user.WorkforceState).ToList()
                : Array.Empty<WorkforceState>());
    }

    internal static ShiftSessionDto ToDto(ShiftSession shift, string displayName) =>
        new(shift.Id,
            shift.UserId,
            displayName,
            shift.ShiftStart,
            shift.ShiftEnd,
            shift.ShiftEnd.HasValue ? shift.ShiftEnd.Value - shift.ShiftStart : null,
            shift.EndedImproperly,
            shift.EndedByUserId,
            shift.EndNote);
}
