using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Notifications;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Verifications.Dtos;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Entities.Verifications;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Verifications.Services;

public interface IVerificationService
{
    /// <summary>Raise a check on something. Needs no request and no task.</summary>
    Task<Result<VerificationDetailDto>> CreateAsync(
        long actorId, CreateVerificationDto request, CancellationToken ct = default);

    /// <summary>Give it to a checker, or move it to a different one.</summary>
    Task<Result<VerificationDetailDto>> AssignAsync(
        long id, long actorId, AssignVerificationDto request, CancellationToken ct = default);

    /// <summary>
    /// A checker takes an unclaimed check for themselves.
    ///
    /// Separate from <see cref="AssignAsync"/> because it answers a different question and needs a
    /// different permission: assigning is a coordinator giving work out, claiming is a checker
    /// picking up something nobody holds. Without it the "needs a checker" notification — which
    /// goes to exactly the people holding <c>Verification.Work</c> — leads to a page they can do
    /// nothing on.
    /// </summary>
    Task<Result<VerificationDetailDto>> ClaimAsync(long id, long actorId, CancellationToken ct = default);

    /// <summary>The checker begins looking. Idempotent.</summary>
    Task<Result<VerificationDetailDto>> StartAsync(long id, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Record what was found. The only route to <see cref="VerificationStatus.Completed"/>, and the
    /// point at which a routed request is handed back to whoever triages it.
    /// </summary>
    Task<Result<VerificationDetailDto>> RecordResultAsync(
        long id, long actorId, RecordVerificationResultDto request, CancellationToken ct = default);

    /// <summary>Call it off. Kept with its reason rather than deleted.</summary>
    Task<Result<VerificationDetailDto>> CancelAsync(
        long id, long actorId, CancelVerificationDto request, CancellationToken ct = default);

    Task<Result<VerificationDetailDto>> GetAsync(
        long id, long userId, IReadOnlySet<string> permissions, CancellationToken ct = default);

    Task<PagedResult<VerificationSummaryDto>> ListAsync(
        long userId, IReadOnlySet<string> permissions, VerificationStatus? status, bool mineOnly,
        PageQuery page, CancellationToken ct = default);

    /// <summary>What is on this checker's desk: assigned or in progress, most urgent first.</summary>
    Task<IReadOnlyList<VerificationSummaryDto>> MyQueueAsync(long userId, CancellationToken ct = default);

    /// <summary>People who can be given a verification — anyone holding <c>Verification.Work</c>.</summary>
    Task<IReadOnlyList<AssignableCheckerDto>> AssignableCheckersAsync(CancellationToken ct = default);

    /// <summary>
    /// Raise a verification out of triage, against a request. Called by
    /// <c>RequestTriageService</c>; the request's own status is that caller's business.
    /// </summary>
    Task<Result<Verification>> RaiseForRequestAsync(
        Request request, long actorId, SendForVerificationDto details, CancellationToken ct = default);

    /// <summary>The verifications raised against a request, newest first. For the request screen.</summary>
    Task<IReadOnlyList<RequestVerificationDto>> ForRequestAsync(long requestId, CancellationToken ct = default);

    /// <summary>Whether anything is still being checked for this request. Triage asks before approving.</summary>
    Task<bool> HasOpenForRequestAsync(long requestId, CancellationToken ct = default);
}

/// <summary>A person a verification can be given to, with how much they are already holding.</summary>
public sealed record AssignableCheckerDto(long UserId, string DisplayName, int OpenVerifications);

/// <summary>
/// Assigned investigation: "go and find out whether this is really broken".
///
/// <para>
/// The rule that shapes every method here is the one in <see cref="RecordResultAsync"/>:
/// <b>a verification never creates work.</b> Even <see cref="VerificationResult.IssueConfirmed"/>
/// creates nothing. It returns the request to <see cref="RequestStatus.InReview"/> with the
/// findings attached and stops, because approving is a decision a reviewer makes with the evidence
/// in front of them — and because <c>TaskCreationService</c>'s monopoly on creating tasks is what
/// makes "a request never auto-becomes a task" an auditable fact rather than an intention.
/// </para>
///
/// <para>
/// Every completed check hands the request back the same way, whatever it found. That is
/// deliberate: five results with five different consequences would be five rules to remember and
/// five places for a request to get stuck. "It has been looked at, here is what they found, you
/// decide" is one rule, and the reviewer already has every triage outcome available to them.
/// </para>
///
/// <para>
/// Distinct from <c>QCService</c>, which answers whether finished work meets its acceptance
/// criteria and owns the task transitions that go with that. Nothing here touches a
/// <c>WorkTask</c>, a <c>QCReview</c> or a work session.
/// </para>
/// </summary>
public sealed class VerificationService : IVerificationService
{
    private readonly IWorkflowDbContext _db;
    private readonly INumberGenerator _numbers;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<VerificationService> _logger;

    public VerificationService(
        IWorkflowDbContext db,
        INumberGenerator numbers,
        IAuditService audit,
        INotificationService notifications,
        IDateTimeProvider clock,
        ILogger<VerificationService> logger)
    {
        _db = db;
        _numbers = numbers;
        _audit = audit;
        _notifications = notifications;
        _clock = clock;
        _logger = logger;
    }

    // --- creating ------------------------------------------------------------------------

    public async Task<Result<VerificationDetailDto>> CreateAsync(
        long actorId, CreateVerificationDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return Result<VerificationDetailDto>.Failure(Error.Validation(
                "verification.title_required", "Say in a few words what needs checking."));

        var targetError = await ValidateTargetAsync(request.TargetType, request.ModuleId, request.TargetName, ct);
        if (targetError is not null) return Result<VerificationDetailDto>.Failure(targetError);

        if (request.AssignToUserId is { } proposed)
        {
            var checkerError = await ValidateCheckerAsync(proposed, ct);
            if (checkerError is not null) return Result<VerificationDetailDto>.Failure(checkerError);
        }

        var verification = new Verification
        {
            VerificationNumber = await _numbers.NextAsync(
                NumberSequences.Verification, NumberSequences.VerificationPrefix, ct),
            Title = request.Title.Trim(),
            Instructions = Clean(request.Instructions),
            ExpectedBehavior = Clean(request.ExpectedBehavior),
            TargetType = request.TargetType,
            ModuleId = request.TargetType == VerificationTargetType.Module ? request.ModuleId : null,
            TargetName = Clean(request.TargetName),
            TargetReference = Clean(request.TargetReference),
            Priority = request.Priority,
            RequestedByUserId = actorId,
            RequestedAt = _clock.UtcNow,
            Status = VerificationStatus.Requested
        };

        _db.Verifications.Add(verification);

        // Saved before the activity row: that row needs the generated id, and an activity stream
        // written against id 0 is the sort of thing nobody notices until the screen is empty.
        await _db.SaveChangesAsync(ct);

        RecordActivity(verification, ActivityType.VerificationRequested, actorId,
            $"Raised: {verification.Title}");

        _audit.Record(
            AuditActions.VerificationRaised,
            actorUserId: actorId,
            entityType: nameof(Verification),
            entityId: verification.Id,
            newValues: new
            {
                verification.VerificationNumber,
                Target = verification.TargetType.ToString(),
                Priority = verification.Priority.ToString()
            });

        if (request.AssignToUserId is { } assignee)
            await ApplyAssignmentAsync(verification, actorId, assignee, reason: null, ct);
        else
            await _notifications.RaiseForPermissionAsync(
                Permissions.VerificationWork, actorId,
                $"{verification.VerificationNumber} needs a checker",
                verification.Title, NotificationService.LinkVerification, verification.Id, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Verification {Number} raised by {ActorId} against {Target}",
            verification.VerificationNumber, actorId, verification.TargetType);

        return await DetailAsync(verification.Id, ct);
    }

    public async Task<Result<Verification>> RaiseForRequestAsync(
        Request request, long actorId, SendForVerificationDto details, CancellationToken ct = default)
    {
        var targetError = await ValidateTargetAsync(
            details.TargetType, details.ModuleId, details.TargetName, ct);
        if (targetError is not null) return Result<Verification>.Failure(targetError);

        if (details.AssignToUserId is { } proposed)
        {
            var checkerError = await ValidateCheckerAsync(proposed, ct);
            if (checkerError is not null) return Result<Verification>.Failure(checkerError);
        }

        var verification = new Verification
        {
            VerificationNumber = await _numbers.NextAsync(
                NumberSequences.Verification, NumberSequences.VerificationPrefix, ct),
            Title = Clean(details.Title) ?? request.Title,
            Instructions = Clean(details.Instructions),
            // The requester's own words are the starting point for the investigation, so they are
            // carried across rather than left for the checker to go and find on another screen.
            ExpectedBehavior = Clean(details.ExpectedBehavior) ?? request.ExpectedResult,
            TargetType = details.TargetType,
            ModuleId = details.TargetType == VerificationTargetType.Module
                ? details.ModuleId
                : null,
            TargetName = Clean(details.TargetName),
            TargetReference = Clean(details.TargetReference),
            // The requester's urgency is advisory here exactly as it is at triage.
            Priority = details.Priority ?? MapUrgency(request.RequestedUrgency),
            RequestId = request.Id,
            RequestedByUserId = actorId,
            RequestedAt = _clock.UtcNow,
            Status = VerificationStatus.Requested
        };

        _db.Verifications.Add(verification);
        await _db.SaveChangesAsync(ct);

        RecordActivity(verification, ActivityType.VerificationRequested, actorId,
            $"Raised from request {request.RequestNumber}");

        _audit.Record(
            AuditActions.VerificationRaised,
            actorUserId: actorId,
            entityType: nameof(Verification),
            entityId: verification.Id,
            newValues: new
            {
                verification.VerificationNumber,
                RequestId = request.Id,
                request.RequestNumber,
                Priority = verification.Priority.ToString()
            });

        if (details.AssignToUserId is { } assignee)
        {
            await ApplyAssignmentAsync(verification, actorId, assignee, reason: null, ct);
        }
        else
        {
            // Nobody has it yet, so it has to reach the people who could pick it up — otherwise a
            // routed request sits waiting on a queue no one is looking at.
            await _notifications.RaiseForPermissionAsync(
                Permissions.VerificationWork, actorId,
                $"{verification.VerificationNumber} needs a checker",
                verification.Title, NotificationService.LinkVerification, verification.Id, ct);
        }

        return Result<Verification>.Success(verification);
    }

    // --- assignment ----------------------------------------------------------------------

    public async Task<Result<VerificationDetailDto>> AssignAsync(
        long id, long actorId, AssignVerificationDto request, CancellationToken ct = default)
    {
        var verification = await _db.Verifications.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (verification is null) return NotFound();

        if (IsFinished(verification.Status))
            return Result<VerificationDetailDto>.Failure(Error.Conflict(
                "verification.already_finished",
                $"This verification is {Lower(verification.Status)} and cannot be reassigned."));

        if (verification.AssignedToUserId == request.AssignToUserId)
            return await DetailAsync(id, ct);   // already theirs; idempotent

        // Same rule the task side applies: taking work off somebody is explained, giving out work
        // nobody yet had is not.
        if (verification.AssignedToUserId is not null && string.IsNullOrWhiteSpace(request.Reason))
            return Result<VerificationDetailDto>.Failure(Error.Validation(
                "verification.reassign_reason_required",
                "Say why this is moving to a different checker."));

        var checkerError = await ValidateCheckerAsync(request.AssignToUserId, ct);
        if (checkerError is not null) return Result<VerificationDetailDto>.Failure(checkerError);

        await ApplyAssignmentAsync(verification, actorId, request.AssignToUserId, Clean(request.Reason), ct);
        await _db.SaveChangesAsync(ct);

        return await DetailAsync(id, ct);
    }

    private async Task ApplyAssignmentAsync(
        Verification verification, long actorId, long checkerId, string? reason, CancellationToken ct)
    {
        var previous = verification.AssignedToUserId;

        verification.AssignedToUserId = checkerId;
        verification.AssignedByUserId = actorId;
        verification.AssignedAt = _clock.UtcNow;

        // Only Requested advances. One already in progress that moves to a new checker keeps
        // InProgress — the work has started, and resetting the status would erase that.
        if (verification.Status == VerificationStatus.Requested)
            verification.Status = VerificationStatus.Assigned;

        var checkerName = await DisplayNameAsync(checkerId, ct);

        RecordActivity(verification, ActivityType.VerificationAssigned, actorId,
            checkerId == actorId ? $"{checkerName} picked this up"
            : previous is null ? $"Assigned to {checkerName}"
            : $"Moved to {checkerName}: {reason}");

        _audit.Record(
            AuditActions.VerificationAssigned,
            actorUserId: actorId,
            entityType: nameof(Verification),
            entityId: verification.Id,
            previousValues: previous is null ? null : new { AssignedToUserId = previous },
            newValues: new { AssignedToUserId = checkerId, Reason = reason });

        _notifications.RaiseFor(
            new long?[] { checkerId }, actorId,
            $"{verification.VerificationNumber} is yours to check",
            verification.Title, NotificationService.LinkVerification, verification.Id);
    }

    public async Task<Result<VerificationDetailDto>> ClaimAsync(
        long id, long actorId, CancellationToken ct = default)
    {
        var verification = await _db.Verifications.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (verification is null) return NotFound();

        if (verification.AssignedToUserId == actorId)
            return await DetailAsync(id, ct);   // already theirs; idempotent

        // Only genuinely unclaimed work can be taken. Once somebody holds it, moving it is a
        // decision about two people's workloads — that goes through AssignAsync, which asks why.
        if (verification.Status != VerificationStatus.Requested)
        {
            return Result<VerificationDetailDto>.Failure(Error.Conflict(
                "verification.already_claimed",
                verification.AssignedToUserId is null
                    ? $"A verification that is {Lower(verification.Status)} cannot be picked up."
                    : "Somebody already has this one. Ask for it to be moved."));
        }

        var checkerError = await ValidateCheckerAsync(actorId, ct);
        if (checkerError is not null) return Result<VerificationDetailDto>.Failure(checkerError);

        await ApplyAssignmentAsync(verification, actorId, actorId, reason: null, ct);
        await _db.SaveChangesAsync(ct);

        return await DetailAsync(id, ct);
    }

    // --- doing it ------------------------------------------------------------------------

    public async Task<Result<VerificationDetailDto>> StartAsync(
        long id, long actorId, CancellationToken ct = default)
    {
        var verification = await _db.Verifications.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (verification is null) return NotFound();

        // Decided on the record, not by a permission attribute: the answer depends on who this
        // particular verification was given to, which an attribute cannot express.
        if (verification.AssignedToUserId != actorId)
            return Result<VerificationDetailDto>.Failure(Error.Forbidden(
                "verification.not_checker", "Only the assigned checker can start this."));

        if (verification.Status == VerificationStatus.InProgress)
            return await DetailAsync(id, ct);   // idempotent

        if (verification.Status != VerificationStatus.Assigned)
            return Result<VerificationDetailDto>.Failure(Error.Conflict(
                "verification.not_startable",
                $"A verification that is {Lower(verification.Status)} cannot be started."));

        verification.Status = VerificationStatus.InProgress;
        verification.StartedAt = _clock.UtcNow;

        RecordActivity(verification, ActivityType.VerificationStarted, actorId, "Checking started");

        await _db.SaveChangesAsync(ct);
        return await DetailAsync(id, ct);
    }

    public async Task<Result<VerificationDetailDto>> RecordResultAsync(
        long id, long actorId, RecordVerificationResultDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Findings))
            return Result<VerificationDetailDto>.Failure(Error.Validation(
                "verification.findings_required", "Say what you found."));

        var verification = await _db.Verifications
            .Include(v => v.Request)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (verification is null) return NotFound();

        if (verification.AssignedToUserId != actorId)
            return Result<VerificationDetailDto>.Failure(Error.Forbidden(
                "verification.not_checker", "Only the assigned checker can record what was found."));

        if (verification.Status is not (VerificationStatus.Assigned or VerificationStatus.InProgress))
            return Result<VerificationDetailDto>.Failure(Error.Conflict(
                "verification.not_reportable",
                $"A verification that is {Lower(verification.Status)} cannot be reported on."));

        var now = _clock.UtcNow;

        verification.Status = VerificationStatus.Completed;
        verification.Result = request.Result;
        verification.Findings = request.Findings.Trim();
        verification.CompletedAt = now;

        // Reported without a separate start — a five-minute check where nobody pressed Start. The
        // clock still ran, so the record says so rather than leaving a null nobody can interpret.
        verification.StartedAt ??= now;

        var resultLabel = StatusLabels.For(request.Result);

        RecordActivity(verification, ActivityType.VerificationCompleted, actorId,
            $"{resultLabel}: {verification.Findings}");

        _audit.Record(
            AuditActions.VerificationCompleted,
            actorUserId: actorId,
            entityType: nameof(Verification),
            entityId: verification.Id,
            newValues: new
            {
                verification.VerificationNumber,
                Result = request.Result.ToString(),
                verification.RequestId
            });

        // Whoever asked for it is waiting on the answer.
        _notifications.RaiseFor(
            new long?[] { verification.RequestedByUserId }, actorId,
            $"{verification.VerificationNumber}: {resultLabel}",
            verification.Findings, NotificationService.LinkVerification, verification.Id);

        // --- and the request it came from, if any ----------------------------------------
        //
        // This is the invariant the whole feature turns on. A confirmed problem does NOT create a
        // task. It goes back to a reviewer with the findings attached, and they approve it — or do
        // not — as an explicit act, through the one path that has ever been allowed to create work.
        // Every other result comes back the same way, for the same reason: the reviewer has all six
        // triage outcomes available and is the one who should be choosing between them.
        if (verification.Request is { } source)
        {
            if (source.Status == RequestStatus.UnderVerification)
                source.Status = RequestStatus.InReview;

            _db.RequestActivities.Add(new RequestActivity
            {
                RequestId = source.Id,
                Type = ActivityType.VerificationCompleted,
                ActorUserId = actorId,
                OccurredAt = now,
                Description = $"{verification.VerificationNumber} reported: {resultLabel}"
            });

            await _notifications.RaiseForPermissionAsync(
                Permissions.TaskReview, actorId,
                $"{source.RequestNumber} is back from checking — {resultLabel}",
                verification.Findings, NotificationService.LinkRequest, source.Id, ct);

            _notifications.RaiseFor(
                new long?[] { source.RequestedByUserId }, actorId,
                $"Your request {source.RequestNumber} has been checked",
                resultLabel, NotificationService.LinkRequest, source.Id);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Verification {Number} completed by {ActorId}: {Result}",
            verification.VerificationNumber, actorId, request.Result);

        return await DetailAsync(id, ct);
    }

    public async Task<Result<VerificationDetailDto>> CancelAsync(
        long id, long actorId, CancelVerificationDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<VerificationDetailDto>.Failure(Error.Validation(
                "verification.cancel_reason_required", "Say why this is being called off."));

        var verification = await _db.Verifications
            .Include(v => v.Request)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (verification is null) return NotFound();

        if (IsFinished(verification.Status))
            return Result<VerificationDetailDto>.Failure(Error.Conflict(
                "verification.already_finished",
                $"This verification is already {Lower(verification.Status)}."));

        var now = _clock.UtcNow;

        verification.Status = VerificationStatus.Cancelled;
        verification.CancellationReason = request.Reason.Trim();
        verification.CompletedAt = now;

        RecordActivity(verification, ActivityType.VerificationCancelled, actorId,
            $"Called off: {verification.CancellationReason}");

        _audit.Record(
            AuditActions.VerificationCancelled,
            actorUserId: actorId,
            entityType: nameof(Verification),
            entityId: verification.Id,
            newValues: new { verification.VerificationNumber, Reason = verification.CancellationReason });

        _notifications.RaiseFor(
            new long?[] { verification.AssignedToUserId, verification.RequestedByUserId }, actorId,
            $"{verification.VerificationNumber} was called off",
            verification.CancellationReason, NotificationService.LinkVerification, verification.Id);

        // A request must never be left waiting on a check that is no longer happening.
        if (verification.Request is { Status: RequestStatus.UnderVerification } source)
        {
            source.Status = RequestStatus.InReview;

            _db.RequestActivities.Add(new RequestActivity
            {
                RequestId = source.Id,
                Type = ActivityType.VerificationCancelled,
                ActorUserId = actorId,
                OccurredAt = now,
                Description = $"{verification.VerificationNumber} was called off: {verification.CancellationReason}"
            });
        }

        await _db.SaveChangesAsync(ct);
        return await DetailAsync(id, ct);
    }

    // --- reading -------------------------------------------------------------------------

    public async Task<Result<VerificationDetailDto>> GetAsync(
        long id, long userId, IReadOnlySet<string> permissions, CancellationToken ct = default)
    {
        var visible = permissions.Contains(Permissions.VerificationViewAll)
            || await _db.Verifications.AnyAsync(
                v => v.Id == id && (v.RequestedByUserId == userId || v.AssignedToUserId == userId), ct);

        // 404 rather than 403, matching the task detail: "you may not see this" still confirms it
        // exists, and a verification number is trivially guessable.
        if (!visible) return NotFound();

        return await DetailAsync(id, ct);
    }

    public async Task<PagedResult<VerificationSummaryDto>> ListAsync(
        long userId, IReadOnlySet<string> permissions, VerificationStatus? status, bool mineOnly,
        PageQuery page, CancellationToken ct = default)
    {
        var query = Scoped(userId, permissions);

        if (status is { } wanted) query = query.Where(v => v.Status == wanted);
        if (mineOnly) query = query.Where(v => v.AssignedToUserId == userId);

        var total = await query.CountAsync(ct);

        var items = await query
            // Unfinished first, then by urgency, then oldest — the order a queue is worked.
            .OrderBy(v => v.Status == VerificationStatus.Completed || v.Status == VerificationStatus.Cancelled)
            .ThenBy(v => v.Priority)
            .ThenBy(v => v.RequestedAt)
            .ThenBy(v => v.Id)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .Select(Summary)
            .ToListAsync(ct);

        return new PagedResult<VerificationSummaryDto>(
            items.Select(Label).ToList(), page.NormalizedPage, page.NormalizedPageSize, total);
    }

    public async Task<IReadOnlyList<VerificationSummaryDto>> MyQueueAsync(
        long userId, CancellationToken ct = default)
    {
        var items = await _db.Verifications.AsNoTracking()
            .Where(v => v.AssignedToUserId == userId)
            .Where(v => v.Status == VerificationStatus.Assigned || v.Status == VerificationStatus.InProgress)
            .OrderBy(v => v.Priority)
            .ThenBy(v => v.RequestedAt)
            .ThenBy(v => v.Id)
            .Select(Summary)
            .ToListAsync(ct);

        return items.Select(Label).ToList();
    }

    public async Task<IReadOnlyList<AssignableCheckerDto>> AssignableCheckersAsync(
        CancellationToken ct = default)
    {
        // Addressed by capability, never by role name — roles are only bundles, and a site that
        // renames or rearranges them must not lose track of who its checkers are.
        var checkers = await _db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .Where(u => _db.UserRoles
                .Where(ur => ur.UserId == u.Id)
                .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
                .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Key)
                .Any(key => key == Permissions.VerificationWork))
            .Select(u => new AssignableCheckerDto(
                u.Id,
                u.DisplayName,
                _db.Verifications.Count(v => v.AssignedToUserId == u.Id
                    && (v.Status == VerificationStatus.Assigned
                        || v.Status == VerificationStatus.InProgress))))
            .ToListAsync(ct);

        // Ordered in memory, not in SQL, and that is not laziness.
        //
        // `OrderBy(c => c.OpenVerifications)` on the *projected* DTO made EF try to re-evaluate the
        // correlated Count inside an ORDER BY over a projection it had not materialised, which it
        // cannot translate — the whole endpoint threw InvalidOperationException on SQL Server. The
        // list is the people who can carry out checks, so it is tens of rows at most and sorting it
        // here costs nothing.
        //
        // Worth knowing: the test suite runs on EF Core InMemory, which happily evaluates anything
        // client-side, so no unit test could have caught this. Only running it against SQL Server did.
        return checkers
            // Lightest load first: the list is used to place work, so it should suggest someone.
            .OrderBy(c => c.OpenVerifications)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<RequestVerificationDto>> ForRequestAsync(
        long requestId, CancellationToken ct = default)
    {
        var rows = await _db.Verifications.AsNoTracking()
            .Where(v => v.RequestId == requestId)
            .OrderByDescending(v => v.RequestedAt)
            .ThenByDescending(v => v.Id)
            .Select(v => new RequestVerificationDto(
                v.Id,
                v.VerificationNumber,
                v.Status,
                "",
                v.AssignedToUserId,
                v.AssignedToUser != null ? v.AssignedToUser.DisplayName : null,
                v.RequestedAt,
                v.CompletedAt,
                v.Result,
                null,
                v.Findings))
            .ToListAsync(ct);

        return rows
            .Select(r => r with
            {
                StatusLabel = StatusLabels.For(r.Status),
                ResultLabel = r.Result is { } result ? StatusLabels.For(result) : null
            })
            .ToList();
    }

    public Task<bool> HasOpenForRequestAsync(long requestId, CancellationToken ct = default) =>
        _db.Verifications.AnyAsync(
            v => v.RequestId == requestId
                && v.Status != VerificationStatus.Completed
                && v.Status != VerificationStatus.Cancelled, ct);

    // --- helpers -------------------------------------------------------------------------

    private IQueryable<Verification> Scoped(long userId, IReadOnlySet<string> permissions)
    {
        var query = _db.Verifications.AsNoTracking();

        // Without ViewAll you see what you raised and what you were given — the same shape the task
        // list uses, and for the same reason: other people's work is noise on your own screen.
        return permissions.Contains(Permissions.VerificationViewAll)
            ? query
            : query.Where(v => v.RequestedByUserId == userId || v.AssignedToUserId == userId);
    }

    /// <summary>
    /// The database half of the projection. <see cref="StatusLabels"/> is a dictionary lookup that
    /// EF cannot translate, so the labels are left blank here and filled in by <see cref="Label"/>
    /// once the rows are in memory — the same split every other list in this codebase uses.
    /// </summary>
    private static readonly Expression<Func<Verification, VerificationSummaryDto>> Summary =
        v => new VerificationSummaryDto(
            v.Id,
            v.VerificationNumber,
            v.Title,
            v.Status,
            "",
            v.Priority,
            v.TargetType,
            v.TargetType == VerificationTargetType.Module && v.Module != null
                ? v.Module.Name
                : (v.TargetName ?? (v.Request != null ? v.Request.Title : v.Title)),
            v.RequestedByUserId,
            v.RequestedByUser.DisplayName,
            v.RequestedAt,
            v.AssignedToUserId,
            v.AssignedToUser != null ? v.AssignedToUser.DisplayName : null,
            v.Result,
            null,
            v.CompletedAt,
            v.RequestId,
            v.Request != null ? v.Request.RequestNumber : null,
            v.Attachments.Count);

    private static VerificationSummaryDto Label(VerificationSummaryDto row) => row with
    {
        StatusLabel = StatusLabels.For(row.Status),
        ResultLabel = row.Result is { } result ? StatusLabels.For(result) : null
    };

    private async Task<Result<VerificationDetailDto>> DetailAsync(long id, CancellationToken ct)
    {
        var row = await _db.Verifications.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                Entity = x,
                RequestedByDisplayName = x.RequestedByUser.DisplayName,
                AssignedToDisplayName = x.AssignedToUser != null ? x.AssignedToUser.DisplayName : null,
                ModuleName = x.Module != null ? x.Module.Name : null,
                RequestNumber = x.Request != null ? x.Request.RequestNumber : null,
                RequestTitle = x.Request != null ? x.Request.Title : null
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return NotFound();

        var activity = await _db.VerificationActivities.AsNoTracking()
            .Where(a => a.VerificationId == id)
            // (OccurredAt, Id) everywhere — two events can share a timestamp, and without the
            // tie-break "the latest" resolves arbitrarily.
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
            .Join(_db.Users, a => a.ActorUserId, u => u.Id, (a, u) => new { a, u.DisplayName })
            .Select(x => new VerificationActivityDto(
                x.a.Id, x.a.Type.ToString(), x.a.ActorUserId, x.DisplayName,
                x.a.OccurredAt, x.a.Description))
            .ToListAsync(ct);

        var attachments = await _db.Attachments.AsNoTracking()
            .Where(a => a.VerificationId == id)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new AttachmentDto(
                a.Id, a.OriginalFileName, a.ContentType, a.SizeBytes, a.UploadedByUserId, a.CreatedAt))
            .ToListAsync(ct);

        var e = row.Entity;

        return Result<VerificationDetailDto>.Success(new VerificationDetailDto(
            e.Id,
            e.VerificationNumber,
            e.Title,
            e.Instructions,
            e.ExpectedBehavior,
            e.Status,
            StatusLabels.For(e.Status),
            e.Priority,
            e.TargetType,
            TargetSummary(e, row.ModuleName, row.RequestTitle),
            e.ModuleId,
            row.ModuleName,
            e.TargetName,
            e.TargetReference,
            e.RequestedByUserId,
            row.RequestedByDisplayName,
            e.RequestedAt,
            e.AssignedToUserId,
            row.AssignedToDisplayName,
            e.AssignedByUserId,
            e.AssignedAt,
            e.StartedAt,
            e.CompletedAt,
            e.Result,
            e.Result is { } result ? StatusLabels.For(result) : null,
            e.Findings,
            e.CancellationReason,
            e.RequestId,
            row.RequestNumber,
            row.RequestTitle,
            activity,
            attachments,
            e.RowVersion is null ? null : Convert.ToBase64String(e.RowVersion)));
    }

    /// <summary>The target in one line, whichever kind it is.</summary>
    private static string TargetSummary(Verification v, string? moduleName, string? requestTitle)
    {
        var name = v.TargetType switch
        {
            VerificationTargetType.Module => moduleName,
            VerificationTargetType.Request => requestTitle ?? v.TargetName,
            _ => v.TargetName
        } ?? v.Title;

        return string.IsNullOrWhiteSpace(v.TargetReference) ? name : $"{name} ({v.TargetReference})";
    }

    private void RecordActivity(
        Verification verification, ActivityType type, long actorId, string description)
    {
        _db.VerificationActivities.Add(new VerificationActivity
        {
            VerificationId = verification.Id,
            Type = type,
            ActorUserId = actorId,
            OccurredAt = _clock.UtcNow,
            // Trimmed to the column width rather than letting a long findings note fail the save —
            // the full text is on the verification itself, which is where anyone would read it.
            Description = description.Length > 1000 ? description[..997] + "..." : description
        });
    }

    private async Task<Error?> ValidateTargetAsync(
        VerificationTargetType targetType, long? moduleId, string? targetName, CancellationToken ct)
    {
        if (targetType == VerificationTargetType.Module)
        {
            if (moduleId is not { } id)
                return Error.Validation("verification.module_required", "Say which module needs checking.");

            return await _db.Modules.AnyAsync(m => m.Id == id, ct)
                ? null
                : Error.NotFound("verification.module_not_found", "That module was not found.");
        }

        // Form and Build name the thing in words, because nothing in this database represents it.
        // Without a name the record says only "check something", which nobody can act on.
        if (targetType is VerificationTargetType.Form or VerificationTargetType.Build
            && string.IsNullOrWhiteSpace(targetName))
        {
            return Error.Validation(
                "verification.target_name_required", "Name the form or build that needs checking.");
        }

        return null;
    }

    private async Task<Error?> ValidateCheckerAsync(long checkerId, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == checkerId, ct);

        if (user is null || !user.IsActive)
            return Error.NotFound("verification.checker_not_found", "That person was not found.");

        // Checked against the permission, not a role name. Assigning to someone who cannot act on
        // it produces a record that looks assigned and can never move.
        var canWork = await _db.UserRoles
            .Where(ur => ur.UserId == checkerId)
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Key)
            .AnyAsync(key => key == Permissions.VerificationWork, ct);

        return canWork
            ? null
            : Error.Validation(
                "verification.checker_cannot_work",
                $"{user.DisplayName} is not able to carry out verifications.");
    }

    private async Task<string> DisplayNameAsync(long userId, CancellationToken ct) =>
        await _db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? $"user {userId}";

    private static bool IsFinished(VerificationStatus status) =>
        status is VerificationStatus.Completed or VerificationStatus.Cancelled;

    private static string Lower(VerificationStatus status) =>
        StatusLabels.For(status).ToLowerInvariant();

    private static Result<VerificationDetailDto> NotFound() =>
        Result<VerificationDetailDto>.Failure(
            Error.NotFound("verification.not_found", "Verification not found."));

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Priority MapUrgency(RequestedUrgency urgency) => urgency switch
    {
        RequestedUrgency.Critical => Priority.Critical,
        RequestedUrgency.High => Priority.High,
        RequestedUrgency.Low => Priority.Low,
        _ => Priority.Normal
    };
}
