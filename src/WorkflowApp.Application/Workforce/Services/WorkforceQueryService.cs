using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Workforce.Dtos;
using WorkflowApp.Domain.Entities.Workforce;
using WorkflowApp.Domain.Workflow;

namespace WorkflowApp.Application.Workforce.Services;

public interface IWorkforceQueryService
{
    /// <summary>Everyone currently on shift, with what they are doing and for how long.</summary>
    Task<ActiveWorkforceDto> GetActiveWorkforceAsync(CancellationToken ct = default);

    Task<Result<DailyTimelineDto>> GetDailyTimelineAsync(long userId, DateOnly date, CancellationToken ct = default);

    Task<Result<PagedResult<ShiftSessionDto>>> GetShiftHistoryAsync(
        long userId, DateOnly? from, DateOnly? to, PageQuery page, CancellationToken ct = default);

    Task<Result<IReadOnlyList<ActivityEventDto>>> GetActivityAsync(
        long userId, DateOnly date, CancellationToken ct = default);
}

public sealed class WorkforceQueryService : IWorkforceQueryService
{
    private readonly IWorkflowDbContext _db;
    private readonly IBusinessCalendar _calendar;
    private readonly IDateTimeProvider _clock;

    public WorkforceQueryService(IWorkflowDbContext db, IBusinessCalendar calendar, IDateTimeProvider clock)
    {
        _db = db;
        _calendar = calendar;
        _clock = clock;
    }

    public async Task<ActiveWorkforceDto> GetActiveWorkforceAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        // An open shift is the definition of "on shift" — the user's WorkforceState alone could be
        // stale if a row was edited out of band.
        var openShifts = await (
            from shift in _db.ShiftSessions.AsNoTracking()
            where shift.ShiftEnd == null
            join user in _db.Users.AsNoTracking() on shift.UserId equals user.Id
            select new
            {
                user.Id,
                user.UserName,
                user.DisplayName,
                user.DepartmentId,
                user.TeamId,
                user.WorkforceState,
                shift.ShiftStart
            })
            .ToListAsync(ct);

        if (openShifts.Count == 0)
            return new ActiveWorkforceDto(now, 0, 0, 0, 0, Array.Empty<ActiveWorkerDto>());

        // One query for everyone's latest state change rather than one per worker.
        var userIds = openShifts.Select(s => s.Id).ToList();

        var stateChanges = await _db.ActivityEvents.AsNoTracking()
            .Where(e => userIds.Contains(e.UserId) && e.ResultingState != null)
            // Id is carried so ties on OccurredAt break the same way everywhere.
            .Select(e => new { e.Id, e.UserId, e.ResultingState, e.OccurredAt })
            .ToListAsync(ct);

        var stateSince = stateChanges
            .GroupBy(e => e.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).First());

        var workers = openShifts
            .Select(s =>
            {
                DateTimeOffset? since = stateSince.TryGetValue(s.Id, out var latest)
                                        && latest.ResultingState == s.WorkforceState
                    ? latest.OccurredAt
                    : null;

                return new ActiveWorkerDto(
                    s.Id,
                    s.UserName,
                    s.DisplayName,
                    s.DepartmentId,
                    s.TeamId,
                    s.WorkforceState,
                    s.ShiftStart,
                    now - s.ShiftStart,
                    since,
                    since.HasValue ? now - since.Value : null);
            })
            .OrderBy(w => w.DisplayName)
            .ToList();

        return new ActiveWorkforceDto(
            now,
            workers.Count,
            workers.Count(w => WorkforceStateMachine.IsProductive(w.State)),
            workers.Count(w => w.State == Domain.Enums.WorkforceState.Available),
            workers.Count(w => WorkforceStateMachine.IsAway(w.State)),
            workers);
    }

    public async Task<Result<DailyTimelineDto>> GetDailyTimelineAsync(
        long userId, DateOnly date, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.DisplayName })
            .FirstOrDefaultAsync(ct);

        if (user is null)
            return Result<DailyTimelineDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        var (dayStart, dayEnd) = _calendar.DayRange(date);

        var events = await _db.ActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAt >= dayStart && e.OccurredAt < dayEnd)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);

        // The state the user was already in at midnight. Without this an overnight shift would lose
        // every hour before the day's first event.
        var carried = await _db.ActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAt < dayStart && e.ResultingState != null)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync(ct);

        // Only carry it in if it left the user on shift — a "Logged Out" from last night should
        // not paint the top of today's timeline.
        if (carried is not null &&
            (carried.ResultingState is null || !WorkforceStateMachine.IsOnShift(carried.ResultingState.Value)))
        {
            carried = null;
        }

        var (entries, onShift, productive, away, byState) =
            DailyTimelineBuilder.Build(events, dayStart, dayEnd, _clock.UtcNow, carried);

        return Result<DailyTimelineDto>.Success(new DailyTimelineDto(
            user.Id, user.DisplayName, date, entries, onShift, productive, away, byState));
    }

    public async Task<Result<PagedResult<ShiftSessionDto>>> GetShiftHistoryAsync(
        long userId, DateOnly? from, DateOnly? to, PageQuery page, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.DisplayName })
            .FirstOrDefaultAsync(ct);

        if (user is null)
            return Result<PagedResult<ShiftSessionDto>>.Failure(Error.NotFound("user.not_found", "User not found."));

        var query = _db.ShiftSessions.AsNoTracking().Where(s => s.UserId == userId);

        if (from.HasValue)
            query = query.Where(s => s.ShiftStart >= _calendar.DayRange(from.Value).Start);

        if (to.HasValue)
            query = query.Where(s => s.ShiftStart < _calendar.DayRange(to.Value).EndExclusive);

        var total = await query.CountAsync(ct);

        var shifts = await query
            .OrderByDescending(s => s.ShiftStart)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync(ct);

        var items = shifts.Select(s => ShiftService.ToDto(s, user.DisplayName)).ToList();

        return Result<PagedResult<ShiftSessionDto>>.Success(
            new PagedResult<ShiftSessionDto>(items, page.NormalizedPage, page.NormalizedPageSize, total));
    }

    public async Task<Result<IReadOnlyList<ActivityEventDto>>> GetActivityAsync(
        long userId, DateOnly date, CancellationToken ct = default)
    {
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct))
            return Result<IReadOnlyList<ActivityEventDto>>.Failure(
                Error.NotFound("user.not_found", "User not found."));

        var (dayStart, dayEnd) = _calendar.DayRange(date);

        var events = await _db.ActivityEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAt >= dayStart && e.OccurredAt < dayEnd)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Select(e => new ActivityEventDto(
                e.Id, e.OccurredAt, e.Label, e.ResultingState, e.RelatedTaskId, e.Note))
            .ToListAsync(ct);

        return Result<IReadOnlyList<ActivityEventDto>>.Success(events);
    }
}
