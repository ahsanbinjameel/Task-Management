using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Dtos;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Identity.Services;

public interface IAuthService
{

    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<Result<AuthResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task<Result> LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task<Result> ChangePasswordAsync(long userId, ChangePasswordRequest request, CancellationToken ct = default);
    Task<Result<UserDto>> GetCurrentUserAsync(long userId, CancellationToken ct = default);
}

/// <summary>
/// Authentication use cases: login, refresh-token rotation, logout, password change.
///
/// Design notes that matter:
/// <list type="bullet">
/// <item>Every login attempt is recorded — successes and failures — for the security audit trail.</item>
/// <item>Failure responses are deliberately uniform ("invalid credentials") so an attacker cannot
/// enumerate valid usernames. The specific reason goes to the audit log, not the caller.</item>
/// <item>Refresh tokens are stored hashed and rotated on every use. Presenting an already-revoked
/// token is treated as theft and revokes the user's whole token family.</item>
/// <item>An auth session is not a shift session — logging in does not start a shift (Phase 2).</item>
/// </list>
/// </summary>
public sealed class AuthService : IAuthService
{
    private const string InvalidCredentialsCode = "auth.invalid_credentials";
    private const string InvalidCredentialsMessage = "The username or password is incorrect.";

    private readonly IWorkflowDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;
    private readonly IActivityLogger _activity;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly AuthOptions _authOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IWorkflowDbContext db,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IPermissionService permissions,
        IAuditService audit,
        IActivityLogger activity,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IOptions<AuthOptions> authOptions,
        ILogger<AuthService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _permissions = permissions;
        _audit = audit;
        _activity = activity;
        _currentUser = currentUser;
        _clock = clock;
        _authOptions = authOptions.Value;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var identifier = request.UserName.Trim();

        // Accept either username or email as the identifier.
        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.UserName == identifier || u.Email == identifier, ct);

        if (user is null)
            return await FailLoginAsync(identifier, "Unknown user", null, ct);

        if (user.LockoutEndAt is { } lockoutEnd && lockoutEnd > now)
            return await FailLoginAsync(identifier, "Account locked out", user.Id, ct);

        if (!user.IsActive)
            return await FailLoginAsync(identifier, "Account deactivated", user.Id, ct);

        var verification = _passwordHasher.Verify(user.PasswordHash, request.Password);
        if (verification == PasswordVerification.Failed)
        {
            user.FailedLoginCount++;

            if (user.FailedLoginCount >= _authOptions.MaxFailedLoginAttempts)
            {
                user.LockoutEndAt = now.AddMinutes(_authOptions.LockoutMinutes);
                _audit.Record(
                    AuditActions.AccountLockedOut,
                    actorUserId: user.Id,
                    entityType: nameof(User),
                    entityId: user.Id,
                    newValues: new { user.LockoutEndAt, user.FailedLoginCount });
                _logger.LogWarning("User {UserId} locked out until {LockoutEnd}", user.Id, user.LockoutEndAt);
            }

            return await FailLoginAsync(identifier, "Bad password", user.Id, ct);
        }

        // Correct password: the hash parameters may have aged out of policy.
        if (verification == PasswordVerification.SuccessRehashNeeded)
            user.PasswordHash = _passwordHasher.Hash(request.Password);

        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        user.LastLoginAt = now;

        // Authentication only establishes an auth session. Shared with demo mode, which mints its
        // own tokens and has to arrive the same way — see WorkforceSignIn for why it is a named
        // rule rather than these four lines.
        WorkforceSignIn.Apply(user, _activity, now);

        _db.LoginAttempts.Add(new LoginAttempt
        {
            UserNameTried = identifier,
            Succeeded = true,
            IpAddress = _currentUser.IpAddress,
            UserAgent = _currentUser.UserAgent
        });

        var response = await IssueTokensAsync(user, ct);

        _audit.Record(
            AuditActions.LoginSucceeded,
            actorUserId: user.Id,
            entityType: nameof(User),
            entityId: user.Id);

        await _db.SaveChangesAsync(ct);
        return Result<AuthResponse>.Success(response);
    }

    public async Task<Result<AuthResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var hash = _tokenService.HashRefreshToken(request.RefreshToken);

        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
            return Result<AuthResponse>.Failure(
                Error.Unauthorized("auth.invalid_refresh_token", "The refresh token is not valid."));

        // A revoked token being presented again means the value leaked (or a client replayed an
        // old one). Kill the whole family — the legitimate user simply logs in again.
        if (stored.RevokedAt is not null)
        {
            await RevokeAllForUserAsync(stored.UserId, now, ct);
            _audit.Record(
                AuditActions.TokenReuseDetected,
                actorUserId: stored.UserId,
                entityType: nameof(RefreshToken),
                entityId: stored.Id);
            await _db.SaveChangesAsync(ct);

            _logger.LogWarning("Refresh token reuse detected for user {UserId}; all tokens revoked", stored.UserId);
            return Result<AuthResponse>.Failure(
                Error.Unauthorized("auth.refresh_token_reused", "The refresh token has already been used."));
        }

        if (stored.ExpiresAt <= now)
            return Result<AuthResponse>.Failure(
                Error.Unauthorized("auth.refresh_token_expired", "The refresh token has expired."));

        var user = stored.User;
        if (!user.IsActive)
            return Result<AuthResponse>.Failure(
                Error.Forbidden("auth.account_inactive", "This account is deactivated."));

        var response = await IssueTokensAsync(user, ct);

        // Rotate: the old token is revoked and points at its replacement, so the chain is auditable.
        stored.RevokedAt = now;
        stored.ReplacedByTokenHash = _tokenService.HashRefreshToken(response.RefreshToken);

        _audit.Record(
            AuditActions.TokenRefreshed,
            actorUserId: user.Id,
            entityType: nameof(RefreshToken),
            entityId: stored.Id);

        await _db.SaveChangesAsync(ct);
        return Result<AuthResponse>.Success(response);
    }

    public async Task<Result> LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var hash = _tokenService.HashRefreshToken(request.RefreshToken);

        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        // Logout is idempotent: an unknown or already-revoked token still reports success so a
        // client can always clear its state.
        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = now;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);

            // Ending the auth session does not end an open shift — that is deliberate. Only the
            // "logged in but never started a shift" state is rolled back here; a user who logs out
            // mid-shift is left for the Phase 2 improper-logout detection to flag.
            if (user is not null && user.WorkforceState == WorkforceState.LoggedInShiftNotStarted)
            {
                user.WorkforceState = WorkforceState.NotLoggedIn;
                _activity.Record(user.Id, ActivityLabels.LoggedOut, WorkforceState.NotLoggedIn, occurredAt: now);
            }
            else if (user is not null)
            {
                // Logged out mid-shift. The shift stays open on purpose — the stale-shift sweep
                // will close and flag it if they never come back.
                _activity.Record(user.Id, ActivityLabels.LoggedOut, occurredAt: now);
            }

            _audit.Record(
                AuditActions.Logout,
                actorUserId: stored.UserId,
                entityType: nameof(RefreshToken),
                entityId: stored.Id);

            await _db.SaveChangesAsync(ct);
        }

        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(long userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result.Failure(Error.NotFound("user.not_found", "User not found."));

        if (_passwordHasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordVerification.Failed)
            return Result.Failure(Error.Unauthorized(InvalidCredentialsCode, "The current password is incorrect."));

        if (PasswordPolicy.Validate(request.NewPassword, _authOptions.MinimumPasswordLength) is { } policyError)
            return Result.Failure(policyError);

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);

        // Changing a password invalidates every issued refresh token — a session established with
        // the old credentials must not survive.
        await RevokeAllForUserAsync(userId, now, ct);

        _audit.Record(
            AuditActions.PasswordChanged,
            actorUserId: userId,
            entityType: nameof(User),
            entityId: userId);

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<UserDto>> GetCurrentUserAsync(long userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<UserDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        var roles = await _permissions.GetRolesAsync(userId, ct);
        var permissions = await _permissions.GetPermissionsAsync(userId, ct);

        return Result<UserDto>.Success(UserMapper.ToDto(user, roles, permissions));
    }

    // --- helpers -------------------------------------------------------------------------

    /// <summary>
    /// Records the failed attempt and returns the same opaque error regardless of cause.
    /// Saves immediately so the attempt and any lockout survive even though the caller fails.
    /// </summary>
    private async Task<Result<AuthResponse>> FailLoginAsync(
        string identifier, string reason, long? userId, CancellationToken ct)
    {
        _db.LoginAttempts.Add(new LoginAttempt
        {
            UserNameTried = identifier,
            Succeeded = false,
            FailureReason = reason,
            IpAddress = _currentUser.IpAddress,
            UserAgent = _currentUser.UserAgent
        });

        _audit.Record(
            AuditActions.LoginFailed,
            actorUserId: userId,
            entityType: nameof(User),
            entityId: userId,
            newValues: new { Reason = reason, Identifier = identifier });

        await _db.SaveChangesAsync(ct);

        return Result<AuthResponse>.Failure(
            Error.Unauthorized(InvalidCredentialsCode, InvalidCredentialsMessage));
    }

    /// <summary>Builds the access + refresh pair and stages the refresh token row.</summary>
    private async Task<AuthResponse> IssueTokensAsync(
        User user,
        CancellationToken ct,
        bool isDemo = false,
        long? demoRealUserId = null,
        string? demoRealUserName = null)
    {
        var roles = await _permissions.GetRolesAsync(user.Id, ct);
        var permissions = await _permissions.GetPermissionsAsync(user.Id, ct);

        var access = _tokenService.CreateAccessToken(
            user, roles, permissions, isDemo, demoRealUserId, demoRealUserName);
        var refresh = _tokenService.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refresh.TokenHash,
            ExpiresAt = refresh.ExpiresAt
        });

        return new AuthResponse(
            access.Token,
            access.ExpiresAt,
            refresh.RawToken,
            refresh.ExpiresAt,
            UserMapper.ToDto(user, roles, permissions));
    }

    /// <summary>Revokes every still-live refresh token for a user. Caller saves.</summary>
    private async Task RevokeAllForUserAsync(long userId, DateTimeOffset now, CancellationToken ct)
    {
        var live = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in live)
        {
            token.RevokedAt = now;
        }
    }
}

/// <summary>Minimum password rules, applied on every path that sets a password.</summary>
public static class PasswordPolicy
{
    /// <summary>Returns an <see cref="Error"/> when the password is unacceptable, otherwise null.</summary>
    public static Error? Validate(string password, int minimumLength)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < minimumLength)
            return Error.Validation(
                "password.too_short",
                $"Password must be at least {minimumLength} characters.");

        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            return Error.Validation(
                "password.too_weak",
                "Password must contain an uppercase letter, a lowercase letter, and a digit.");

        return null;
    }
}

/// <summary>
/// Maps a user onto the shape a client gets. Public because demo mode mints tokens outside
/// AuthService and needs the same projection — one mapping, so a demo session and a live one
/// describe a user identically.
/// </summary>
public static class UserMapper
{
    public static UserDto ToDto(User user, IReadOnlyList<string> roles, IReadOnlyList<string> permissions) =>
        new(user.Id,
            user.UserName,
            user.Email,
            user.DisplayName,
            user.IsActive,
            user.WorkforceState.ToString(),
            user.LastLoginAt,
            user.DepartmentId,
            user.TeamId,
            roles,
            permissions);
}
