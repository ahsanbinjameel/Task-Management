using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Requests.Services;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Workflow;

namespace WorkflowApp.Application.Tasks.Services;

public interface IQuickWorkService
{
    Task<Result<QuickWorkDto>> StartAsync(long userId, StartQuickWorkDto request, CancellationToken ct = default);
    Task<Result<QuickWorkDto>> FinishAsync(long id, long userId, FinishQuickWorkDto request, CancellationToken ct = default);
    Task<Result<QuickWorkDto>> CancelAsync(long id, long userId, CancellationToken ct = default);

    /// <summary>Raise a request out of quick work that turned out to be bigger than five minutes.</summary>
    Task<Result<QuickWorkDto>> PromoteAsync(long id, long userId, PromoteQuickWorkDto request, CancellationToken ct = default);

    /// <summary>The caller's running quick work, if any.</summary>
    Task<QuickWorkDto?> ActiveAsync(long userId, CancellationToken ct = default);

    /// <summary>Everything this person did outside the workflow on a given business day.</summary>
    Task<IReadOnlyList<QuickWorkDto>> ForDayAsync(long userId, DateOnly date, CancellationToken ct = default);
}

/// <summary>
/// The clock for work that never came through the front door.
///
/// Three rules shape this service, and they are all about not being a back door:
///
/// <list type="number">
/// <item>
/// <b>One thing at a time still holds.</b> Starting quick work pauses the running task through the
/// same close-then-open sequence the task interrupt uses, in one commit. The interrupted session
/// keeps its recorded time, the task goes to <c>Paused</c> and not <c>Blocked</c> (nothing is wrong
/// with it, it simply waited), and the worker stays <c>Working</c> — because they are working.
/// </item>
/// <item>
/// <b>Promotion produces a request, never a task.</b> <c>TaskCreationService</c> keeps its monopoly.
/// Ten minutes on the phone that turns out to be a fortnight of work still has to be reviewed like
/// anything else; what promotion saves is the retyping, not the review.
/// </item>
/// <item>
/// <b>An outcome is required to finish.</b> A row saying somebody was busy for forty minutes with
/// no record of what came of it is worse than no row: it inflates the day's total and answers
/// nothing. Quick work started by mistake is cancelled, and cancelled time is reported separately
/// rather than counted.
/// </item>
/// </list>
/// </summary>
public sealed class QuickWorkService : IQuickWorkService
{
    private readonly IWorkflowDbContext _db;
    private readonly IWorkSessionService _sessions;
    private readonly IRequestService _requests;
    private readonly ILookupService _lookups;
    private readonly IActivityLogger _activity;
    private readonly IBusinessCalendar _calendar;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<QuickWorkService> _logger;

    public QuickWorkService(
        IWorkflowDbContext db,
        IWorkSessionService sessions,
        IRequestService requests,
        ILookupService lookups,
        IActivityLogger activity,
        IBusinessCalendar calendar,
        IDateTimeProvider clock,
        ILogger<QuickWorkService> logger)
    {
        _db = db;
        _sessions = sessions;
        _requests = requests;
        _lookups = lookups;
        _activity = activity;
        _calendar = calendar;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<QuickWorkDto>> StartAsync(
        long userId, StartQuickWorkDto request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        if (string.IsNullOrWhiteSpace(request.Title))
            return Result<QuickWorkDto>.Failure(Error.Validation(
                "quickwork.title_required", "Say in a few words what this is."));

        // Same rule as the task timer: time cannot be recorded against a day nobody is on.
        if (!await _db.ShiftSessions.AnyAsync(s => s.UserId == userId && s.ShiftEnd == null, ct))
            return Result<QuickWorkDto>.Failure(Error.Conflict(
                "shift.not_open", "Start your shift before recording work."));

        var running = await _db.QuickWork
            .FirstOrDefaultAsync(q => q.UserId == userId && q.Status == QuickWorkStatus.Active, ct);

        if (running is not null)
            return Result<QuickWorkDto>.Failure(Error.Conflict(
                "quickwork.already_active",
                $"You are already recording \"{running.Title}\". Finish that first."));

        // Whatever task was running is put down first. Not optional, and not a separate call the
        // client could forget: the interrupted session has to close in the same commit that opens
        // this one, or the day briefly shows two things running at once.
        var activeSession = await _db.WorkSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == WorkSessionStatus.Active, ct);

        long? interruptedTaskId = null;

        if (activeSession is not null)
        {
            var interrupted = await _db.Tasks.FirstAsync(t => t.Id == activeSession.TaskId, ct);
            interruptedTaskId = interrupted.Id;

            activeSession.SessionEnd = now;
            activeSession.Status = WorkSessionStatus.Paused;
            activeSession.EndPauseReasonId = request.PauseReasonId;
            activeSession.EndComment = $"Interrupted by: {request.Title.Trim()}";
            activeSession.EndedByInterruption = true;

            // InterruptedByTaskId stays null on purpose. It means "displaced by that task", and
            // filling it with a quick-work id would make every reader of the column wrong.

            TaskStatusJournal.Write(
                _db, _activity, interrupted, WorkTaskStatus.Paused, userId, now,
                reason: $"Interrupted by: {request.Title.Trim()}",
                ActivityType.TaskInterrupted,
                $"Paused — interrupted by other work: {request.Title.Trim()}.");
        }

        var quick = new QuickWork
        {
            Title = request.Title.Trim(),
            UserId = userId,
            StartedAt = now,
            Status = QuickWorkStatus.Active,
            ClientId = await _lookups.ResolveClientAsync(request.ClientName, ct),
            InterruptedTaskId = interruptedTaskId,
        };

        _db.QuickWork.Add(quick);

        // The worker is working, and the day's report should say so — this time reading as idle is
        // exactly the gap quick work exists to close. The state machine still governs the move: if
        // Working is not reachable from where they are, their state is left alone rather than
        // forced somewhere the machine forbids.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null && WorkforceStateMachine.IsAllowed(user.WorkforceState, WorkforceState.Working))
            user.WorkforceState = WorkforceState.Working;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "User {UserId} started quick work {Title}, interrupting task {TaskId}",
            userId, quick.Title, interruptedTaskId);

        return await ProjectAsync(quick.Id, ct);
    }

    public async Task<Result<QuickWorkDto>> FinishAsync(
        long id, long userId, FinishQuickWorkDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Outcome))
            return Result<QuickWorkDto>.Failure(Error.Validation(
                "quickwork.outcome_required", "Say what came of it before finishing."));

        var quick = await FindOwnAsync(id, userId, ct);
        if (quick.IsFailure) return Result<QuickWorkDto>.Failure(quick.Error!);

        var record = quick.Value!;

        if (record.Status != QuickWorkStatus.Active)
            return Result<QuickWorkDto>.Failure(Error.Conflict(
                "quickwork.not_active", "This is already finished."));

        record.EndedAt = _clock.UtcNow;
        record.Status = QuickWorkStatus.Finished;
        record.Outcome = request.Outcome.Trim();

        await _db.SaveChangesAsync(ct);

        // Handing the work back is a separate operation on purpose: it goes through the ordinary
        // start path, so the dependency check, the workflow map and the one-active-session rule all
        // apply exactly as they would if the person had clicked Start themselves. A failure there
        // must not undo the finish, which is why this is after the save and its result is ignored
        // beyond logging — the quick work is genuinely finished either way.
        if (request.ResumeInterruptedTask && record.InterruptedTaskId is { } taskId)
        {
            var resumed = await _sessions.StartAsync(taskId, userId, ct);

            if (resumed.IsFailure)
            {
                _logger.LogInformation(
                    "Quick work {Id} finished but task {TaskId} could not be resumed: {Code}",
                    id, taskId, resumed.Error!.Code);
            }
        }

        return await ProjectAsync(record.Id, ct);
    }

    public async Task<Result<QuickWorkDto>> CancelAsync(long id, long userId, CancellationToken ct = default)
    {
        var quick = await FindOwnAsync(id, userId, ct);
        if (quick.IsFailure) return Result<QuickWorkDto>.Failure(quick.Error!);

        var record = quick.Value!;

        if (record.Status != QuickWorkStatus.Active)
            return Result<QuickWorkDto>.Failure(Error.Conflict(
                "quickwork.not_active", "This is already finished."));

        // Kept, not deleted. History is append-only here too, and a cancelled row is the honest
        // record of a mis-click — it simply does not count towards the day.
        record.EndedAt = _clock.UtcNow;
        record.Status = QuickWorkStatus.Cancelled;

        await _db.SaveChangesAsync(ct);
        return await ProjectAsync(record.Id, ct);
    }

    public async Task<Result<QuickWorkDto>> PromoteAsync(
        long id, long userId, PromoteQuickWorkDto request, CancellationToken ct = default)
    {
        var quick = await FindOwnAsync(id, userId, ct);
        if (quick.IsFailure) return Result<QuickWorkDto>.Failure(quick.Error!);

        var record = quick.Value!;

        if (record.PromotedToRequestId is not null)
            return Result<QuickWorkDto>.Failure(Error.Conflict(
                "quickwork.already_promoted", "A request has already been raised from this."));

        var clientName = record.ClientId is { } clientId
            ? await _db.Clients.AsNoTracking().Where(c => c.Id == clientId)
                .Select(c => c.Name).FirstOrDefaultAsync(ct)
            : null;

        // Raised in the name of whoever did the quick work. They are the one who can answer a
        // reviewer's questions about it — the caller who phoned may not even have an account.
        var created = await _requests.CreateAsync(userId, new CreateRequestDto
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? record.Title : request.Title.Trim(),
            Description = request.Description.Trim(),
            Type = request.Type,
            RequestedUrgency = request.RequestedUrgency,
            ClientName = clientName,
        }, ct);

        if (created.IsFailure) return Result<QuickWorkDto>.Failure(created.Error!);

        record.PromotedToRequestId = created.Value!.Id;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Quick work {Id} promoted to request {RequestNumber}", id, created.Value.RequestNumber);

        return await ProjectAsync(record.Id, ct);
    }

    public async Task<QuickWorkDto?> ActiveAsync(long userId, CancellationToken ct = default)
    {
        var id = await _db.QuickWork.AsNoTracking()
            .Where(q => q.UserId == userId && q.Status == QuickWorkStatus.Active)
            .Select(q => (long?)q.Id)
            .FirstOrDefaultAsync(ct);

        if (id is null) return null;

        var result = await ProjectAsync(id.Value, ct);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<IReadOnlyList<QuickWorkDto>> ForDayAsync(
        long userId, DateOnly date, CancellationToken ct = default)
    {
        // Clipped to the *business* day, like every other daily figure, so an evening call lands on
        // the day the person was working rather than on tomorrow's report.
        var (start, end) = _calendar.DayRange(date);

        return await Query()
            .Where(q => q.UserId == userId && q.StartedAt >= start && q.StartedAt < end)
            .OrderBy(q => q.StartedAt)
            .Select(Projection(_clock.UtcNow))
            .ToListAsync(ct);
    }

    // --- shared ------------------------------------------------------------------------------

    /// <summary>
    /// No <c>Include</c>: the projection reaches through the navigations itself, so EF emits the
    /// joins it needs and nothing more. Including them as well would load whole entities the
    /// projection then throws away.
    /// </summary>
    private IQueryable<QuickWork> Query() => _db.QuickWork.AsNoTracking();

    private async Task<Result<QuickWork>> FindOwnAsync(long id, long userId, CancellationToken ct)
    {
        var record = await _db.QuickWork.FirstOrDefaultAsync(q => q.Id == id, ct);

        if (record is null)
            return Result<QuickWork>.Failure(Error.NotFound("quickwork.not_found", "Not found."));

        // Quick work is a personal record of somebody's own time. Nobody else edits it — not even
        // a supervisor, who would be rewriting an account of a day they were not part of.
        if (record.UserId != userId)
            return Result<QuickWork>.Failure(Error.Forbidden(
                "quickwork.not_owner", "This is somebody else's record."));

        return Result<QuickWork>.Success(record);
    }

    private async Task<Result<QuickWorkDto>> ProjectAsync(long id, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var dto = await Query()
            .Where(q => q.Id == id)
            .Select(Projection(now))
            .FirstOrDefaultAsync(ct);

        return dto is null
            ? Result<QuickWorkDto>.Failure(Error.NotFound("quickwork.not_found", "Not found."))
            : Result<QuickWorkDto>.Success(dto);
    }

    /// <summary>
    /// One projection, used by every read.
    ///
    /// An <see cref="Expression"/> rather than a method, so it composes into the query and runs in
    /// the database — a static call inside a <c>Select</c> would not translate, and materialising
    /// the rows first to call it would quietly turn every list into a client-side evaluation.
    ///
    /// A running record's duration climbs to <paramref name="now"/>, so the screen and the report
    /// agree without either of them doing arithmetic of its own.
    /// </summary>
    private static Expression<Func<QuickWork, QuickWorkDto>> Projection(DateTimeOffset now) =>
        q => new QuickWorkDto(
            q.Id,
            q.Title,
            q.UserId,
            q.User.DisplayName,
            q.StartedAt,
            q.EndedAt,
            (q.EndedAt ?? now) - q.StartedAt,
            q.Status,
            q.ClientId,
            q.Client != null ? q.Client.Name : null,
            q.Outcome,
            q.InterruptedTaskId,
            q.InterruptedTask != null ? q.InterruptedTask.TaskNumber : null,
            q.PromotedToRequestId,
            q.PromotedToRequest != null ? q.PromotedToRequest.RequestNumber : null);
}
