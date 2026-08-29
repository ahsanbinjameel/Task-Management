using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Demo;
using WorkflowApp.Application.Identity.Dtos;
using WorkflowApp.Application.Identity.Services;

namespace WorkflowApp.Api.Controllers;

public sealed record EnterDemoRequest
{
    /// <summary>Which of the cast to start as. Defaults to the first, which is the requester.</summary>
    public long? DemoUserId { get; init; }
}

public sealed record SwitchDemoRequest
{
    public long DemoUserId { get; init; }
}

/// <summary>
/// Demo mode: the same application, the same business logic, a different database.
///
/// The token these endpoints issue carries a <c>demo</c> claim, and that claim is the only thing
/// that makes a demonstration a demonstration — it selects the demo catalog when the DbContext is
/// built, and every service above it runs unchanged and unaware. So a request raised in a demo is
/// raised by the real <c>RequestService</c>, approved by the real triage rules, and constrained by
/// the real filtered indexes. What it is not is a second implementation to keep in step.
///
/// Two things worth knowing about the sessions.
///
/// A demo session gets an access token and <b>no refresh token</b>. That is deliberate: refreshing
/// issues a fresh token, and a refresh path that forgot to carry the demo claim would hand a demo
/// user a live-database session — the one failure this feature must not have. Rather than defend
/// that path, it does not exist. A demo that runs past the token lifetime drops the operator back
/// into their own account, which is the safe direction to fail in.
///
/// And exiting is a client-side restore of the tokens it kept, not a re-issue. There is nothing to
/// re-authenticate: the real session was never given up.
/// </summary>
[Route("api/demo")]
public sealed class DemoController : ApiControllerBase
{
    private readonly IDemoEnvironment _demo;
    private readonly IDemoSession _session;
    private readonly ITokenService _tokens;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IWorkflowDbContext _db;
    private readonly ILogger<DemoController> _logger;

    public DemoController(
        IDemoEnvironment demo,
        IDemoSession session,
        ITokenService tokens,
        ICurrentUser currentUser,
        IAuditService audit,
        IWorkflowDbContext db,
        ILogger<DemoController> logger)
    {
        _demo = demo;
        _session = session;
        _tokens = tokens;
        _currentUser = currentUser;
        _audit = audit;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Whether a demonstration can be run, who is in the cast, and whether one is running now.
    ///
    /// Open to any signed-in caller so the header can decide whether to show the control at all,
    /// rather than offering it and refusing. The cast is names and roles of fictional accounts in a
    /// disposable catalog, which is not information worth gating.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(DemoStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        // Two different questions. "Can this person start one" is the permission, and it is asked of
        // the live account. "Is one running" is the token — and inside a demonstration the caller is
        // a demo user who deliberately holds no administrative permission at all, so asking them
        // would empty the switcher at exactly the moment it is needed. Being in a demo is itself the
        // proof that somebody who was allowed to started it.
        var canStart = _demo.IsConfigured && User.HasClaim("permission", Permissions.AdminDemoMode);
        var available = canStart || _session.IsActive;

        // Only read when somebody could actually use it: touching the catalog on the off-chance
        // would create a database on every machine that ever starts the application.
        var cast = available ? await SafeCastAsync(ct) : Array.Empty<DemoUserDto>();

        return Ok(new DemoStatusDto(
            available,
            _session.IsActive,
            _session.IsActive ? _currentUser.UserName : null,
            _session.RealUserName,
            cast));
    }

    /// <summary>Start a demonstration. Brings the demo catalog up first if it is not there yet.</summary>
    [HttpPost("enter")]
    [HasPermission(Permissions.AdminDemoMode)]
    [ProducesResponseType(typeof(DemoTokenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Enter([FromBody] EnterDemoRequest? request, CancellationToken ct)
    {
        if (!_demo.IsConfigured)
            return Problem(statusCode: 409, detail: "No demo database is configured.");

        if (_session.IsActive)
            return Problem(statusCode: 409, detail: "You are already in demo mode.");

        await _demo.EnsureReadyAsync(ct);

        var cast = await _demo.CastAsync(ct);
        if (cast.Count == 0)
            return Problem(statusCode: 409, detail: "The demo environment has no users.");

        var chosen = request?.DemoUserId ?? cast[0].Id;

        // The real account is captured here, from the live session, and travels on the demo token
        // so the header can name where exiting returns to.
        var realUserId = CurrentUserId;
        var realUserName = _currentUser.UserName;

        var token = await IssueAsync(chosen, realUserId, realUserName, ct);
        if (token is null) return Problem(statusCode: 404, detail: "That demo user does not exist.");

        // Recorded in the *live* catalog, because this is a real administrator doing a real thing.
        // Everything they then do inside the demonstration is recorded in the demo catalog, where it
        // belongs and where Reset will take it away again.
        _audit.Record(
            AuditActions.DemoStarted, actorUserId: realUserId,
            newValues: new { StartedAs = token.User.UserName });
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} entered demo mode as {DemoUser}", realUserId, token.User.UserName);
        return Ok(token);
    }

    /// <summary>
    /// Change which member of the cast is being shown, without signing in or out.
    ///
    /// Callable only from inside a demonstration, and the real account is carried across from the
    /// current token rather than taken from the request — so switching cannot be used to change
    /// whose session it is on the way back out.
    /// </summary>
    [HttpPost("switch")]
    [ProducesResponseType(typeof(DemoTokenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Switch([FromBody] SwitchDemoRequest request, CancellationToken ct)
    {
        if (!_session.IsActive)
            return Problem(statusCode: 409, detail: "You are not in demo mode.");

        var token = await IssueAsync(
            request.DemoUserId, _session.RealUserId, _session.RealUserName, ct);

        if (token is null) return Problem(statusCode: 404, detail: "That demo user does not exist.");

        _logger.LogInformation(
            "Demo session switched to {DemoUser} by {UserId}", token.User.UserName, _session.RealUserId);

        return Ok(token);
    }

    /// <summary>
    /// Note that a demonstration has ended.
    ///
    /// The client already holds its own session and restores it without help, so this records the
    /// fact and nothing else. It is a POST rather than a GET because it writes, and it is
    /// deliberately forgiving: a demonstration that ended because a laptop closed should not leave
    /// anything broken behind it.
    /// </summary>
    [HttpPost("exit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Exit(CancellationToken ct)
    {
        if (_session.IsActive && _session.RealUserId is { } realUserId)
        {
            _audit.Record(AuditActions.DemoEnded, actorUserId: realUserId);

            // Written to the demo catalog, because that is where this request's context points.
            // The matching Demo.Started row is in the live one; both are true where they are.
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    /// <summary>
    /// Put the demo environment back to a clean starting state.
    ///
    /// Drops and rebuilds the demo catalog. It cannot reach live data by construction rather than by
    /// care: <see cref="IDemoEnvironment"/> holds one connection string and it is the demo one, so
    /// there is no argument anybody could pass that would point this anywhere else.
    /// </summary>
    [HttpPost("reset")]
    [HasPermission(Permissions.AdminDemoMode)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        if (!_demo.IsConfigured)
            return Problem(statusCode: 409, detail: "No demo database is configured.");

        await _demo.ResetAsync(ct);

        _logger.LogWarning("Demo environment reset by user {UserId}", CurrentUserId);
        return NoContent();
    }

    /// <summary>Mints the demo access token. No refresh token — see the note on this class.</summary>
    private async Task<DemoTokenDto?> IssueAsync(
        long demoUserId, long? realUserId, string? realUserName, CancellationToken ct)
    {
        var principal = await _demo.FindAsync(demoUserId, ct);
        if (principal is null) return null;

        // Being handed a token is this account arriving, so the workforce state has to move the way
        // it does on a real sign-in. Without it the cast stayed in NotLoggedIn, which has no
        // transition to Available — so a demo worker was refused their own shift, and with no shift
        // the task timer refused too. Done for switching as well as entering: both are an arrival.
        await _demo.SignInAsync(demoUserId, ct);

        var access = _tokens.CreateAccessToken(
            principal.User, principal.Roles, principal.Permissions,
            isDemo: true, demoRealUserId: realUserId, demoRealUserName: realUserName);

        return new DemoTokenDto(
            access.Token,
            access.ExpiresAt,
            UserMapper.ToDto(principal.User, principal.Roles, principal.Permissions));
    }

    /// <summary>
    /// The cast without building the catalog. A machine that has never run a demonstration should
    /// not create a database just because somebody loaded a page with the control on it.
    /// </summary>
    private async Task<IReadOnlyList<DemoUserDto>> SafeCastAsync(CancellationToken ct)
    {
        try
        {
            return await _demo.CastAsync(ct);
        }
        catch (Exception ex)
        {
            // Absent or unreachable is the ordinary case before the first demonstration, not a fault.
            _logger.LogDebug(ex, "Demo catalog not readable yet; it will be built on entry.");
            return Array.Empty<DemoUserDto>();
        }
    }
}

/// <summary>
/// A demo session: an access token and the user it belongs to.
///
/// Deliberately not <c>AuthResponse</c>, which carries a refresh token. A demo session has none —
/// see the note on <see cref="DemoController"/> for why that absence is the safety property.
/// </summary>
public sealed record DemoTokenDto(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);
