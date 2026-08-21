using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

public class AuthServiceTests
{
    private const string GoodPassword = "CorrectHorse1";

    private static async Task<TestHarness> ReadyAsync()
    {
        var harness = new TestHarness();
        await harness.SeedRolesAndPermissionsAsync();
        return harness;
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_tokens_and_permissions()
    {
        using var h = await ReadyAsync();
        await h.CreateUserAsync(roles: DefaultRoles.Worker);

        var result = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RefreshToken));
        Assert.Contains(Permissions.TaskWork, result.Value.User.Permissions);
        Assert.Contains(DefaultRoles.Worker, result.Value.User.Roles);
    }

    [Fact]
    public async Task Login_accepts_email_as_the_identifier()
    {
        using var h = await ReadyAsync();
        await h.CreateUserAsync();

        var result = await h.Auth.LoginAsync(
            new LoginRequest { UserName = "worker1@workflowapp.local", Password = GoodPassword });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Login_records_an_attempt_whether_it_succeeds_or_fails()
    {
        using var h = await ReadyAsync();
        await h.CreateUserAsync();

        await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });
        await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = "wrong-password" });

        var attempts = await h.Db.LoginAttempts.ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Contains(attempts, a => a.Succeeded);
        Assert.Contains(attempts, a => !a.Succeeded);
    }

    [Fact]
    public async Task Login_failure_message_is_identical_for_unknown_user_and_bad_password()
    {
        using var h = await ReadyAsync();
        await h.CreateUserAsync();

        var unknownUser = await h.Auth.LoginAsync(new LoginRequest { UserName = "nobody", Password = GoodPassword });
        var badPassword = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = "wrong-password" });

        // Username enumeration defence: the caller must not be able to tell these apart.
        Assert.Equal(unknownUser.Error!.Code, badPassword.Error!.Code);
        Assert.Equal(unknownUser.Error.Message, badPassword.Error.Message);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_and_the_lockout_expires()
    {
        using var h = await ReadyAsync();
        var user = await h.CreateUserAsync();

        for (var i = 0; i < h.AuthOptions.MaxFailedLoginAttempts; i++)
            await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = "wrong-password" });

        var locked = await h.Db.Users.FirstAsync(u => u.Id == user.Id);
        Assert.NotNull(locked.LockoutEndAt);

        // Correct password is still refused while the lockout stands.
        var duringLockout = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });
        Assert.True(duringLockout.IsFailure);

        h.Clock.Advance(TimeSpan.FromMinutes(h.AuthOptions.LockoutMinutes + 1));

        var afterLockout = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });
        Assert.True(afterLockout.IsSuccess);

        var cleared = await h.Db.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Null(cleared.LockoutEndAt);
        Assert.Equal(0, cleared.FailedLoginCount);
    }

    [Fact]
    public async Task Lockout_is_audited()
    {
        using var h = await ReadyAsync();
        await h.CreateUserAsync();

        for (var i = 0; i < h.AuthOptions.MaxFailedLoginAttempts; i++)
            await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = "wrong-password" });

        Assert.True(await h.Db.AuditLogs.AnyAsync(a => a.Action == AuditActions.AccountLockedOut));
    }

    [Fact]
    public async Task Deactivated_account_cannot_log_in()
    {
        using var h = await ReadyAsync();
        await h.CreateUserAsync(isActive: false);

        var result = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
    }

    [Fact]
    public async Task Login_moves_user_to_logged_in_but_does_not_start_a_shift()
    {
        using var h = await ReadyAsync();
        var user = await h.CreateUserAsync();

        await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });

        var reloaded = await h.Db.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal(WorkforceState.LoggedInShiftNotStarted, reloaded.WorkforceState);
        // Auth session != shift session: authenticating must not open a shift.
        Assert.False(await h.Db.ShiftSessions.AnyAsync());
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_revokes_the_old_one()
    {
        using var h = await ReadyAsync();
        await h.CreateUserAsync();

        var login = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });
        var original = login.Value!.RefreshToken;

        var refreshed = await h.Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = original });

        Assert.True(refreshed.IsSuccess);
        Assert.NotEqual(original, refreshed.Value!.RefreshToken);

        var originalHash = h.TokenService.HashRefreshToken(original);
        var stored = await h.Db.RefreshTokens.FirstAsync(t => t.TokenHash == originalHash);
        Assert.NotNull(stored.RevokedAt);
        Assert.Equal(h.TokenService.HashRefreshToken(refreshed.Value.RefreshToken), stored.ReplacedByTokenHash);
    }

    [Fact]
    public async Task Reusing_a_revoked_refresh_token_revokes_the_whole_family()
    {
        using var h = await ReadyAsync();
        await h.CreateUserAsync();

        var login = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });
        var original = login.Value!.RefreshToken;

        var rotated = await h.Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = original });
        Assert.True(rotated.IsSuccess);

        // Replaying the already-rotated token is the signature of a stolen token.
        var replay = await h.Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = original });
        Assert.True(replay.IsFailure);
        Assert.Equal("auth.refresh_token_reused", replay.Error!.Code);

        // The replacement must be dead too, otherwise the thief keeps a working session.
        var stillLive = await h.Db.RefreshTokens.CountAsync(t => t.RevokedAt == null);
        Assert.Equal(0, stillLive);
        Assert.True(await h.Db.AuditLogs.AnyAsync(a => a.Action == AuditActions.TokenReuseDetected));
    }

    [Fact]
    public async Task Expired_refresh_token_is_rejected()
    {
        using var h = await ReadyAsync();
        await h.CreateUserAsync();

        var login = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });

        h.Clock.Advance(TimeSpan.FromDays(15));

        var result = await h.Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = login.Value!.RefreshToken });

        Assert.True(result.IsFailure);
        Assert.Equal("auth.refresh_token_expired", result.Error!.Code);
    }

    [Fact]
    public async Task Unknown_refresh_token_is_rejected()
    {
        using var h = await ReadyAsync();

        var result = await h.Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = "not-a-real-token" });

        Assert.True(result.IsFailure);
        Assert.Equal("auth.invalid_refresh_token", result.Error!.Code);
    }

    [Fact]
    public async Task Logout_revokes_the_token_and_is_idempotent()
    {
        using var h = await ReadyAsync();
        await h.CreateUserAsync();

        var login = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });
        var token = login.Value!.RefreshToken;

        Assert.True((await h.Auth.LogoutAsync(new RefreshTokenRequest { RefreshToken = token })).IsSuccess);
        // A client retrying logout, or logging out twice, must not see an error.
        Assert.True((await h.Auth.LogoutAsync(new RefreshTokenRequest { RefreshToken = token })).IsSuccess);
        Assert.True((await h.Auth.LogoutAsync(new RefreshTokenRequest { RefreshToken = "garbage" })).IsSuccess);

        Assert.Equal(0, await h.Db.RefreshTokens.CountAsync(t => t.RevokedAt == null));
    }

    [Fact]
    public async Task Logout_does_not_end_an_open_shift()
    {
        using var h = await ReadyAsync();
        var user = await h.CreateUserAsync();

        var login = await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });

        // Simulate Phase 2 having started a shift.
        var working = await h.Db.Users.FirstAsync(u => u.Id == user.Id);
        working.WorkforceState = WorkforceState.Working;
        await h.Db.SaveChangesAsync();

        await h.Auth.LogoutAsync(new RefreshTokenRequest { RefreshToken = login.Value!.RefreshToken });

        var reloaded = await h.Db.Users.FirstAsync(u => u.Id == user.Id);
        // Ending the auth session must leave the shift alone — they are separate concepts.
        Assert.Equal(WorkforceState.Working, reloaded.WorkforceState);
    }

    [Fact]
    public async Task Change_password_requires_the_current_password()
    {
        using var h = await ReadyAsync();
        var user = await h.CreateUserAsync();

        var result = await h.Auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "not-the-password",
            NewPassword = "BrandNewPass1"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
    }

    [Fact]
    public async Task Change_password_enforces_the_policy()
    {
        using var h = await ReadyAsync();
        var user = await h.CreateUserAsync();

        var tooShort = await h.Auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = GoodPassword,
            NewPassword = "Short1"
        });

        Assert.True(tooShort.IsFailure);
        Assert.Equal("password.too_short", tooShort.Error!.Code);
    }

    [Fact]
    public async Task Change_password_revokes_every_live_refresh_token()
    {
        using var h = await ReadyAsync();
        var user = await h.CreateUserAsync();

        await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });
        await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword });
        Assert.Equal(2, await h.Db.RefreshTokens.CountAsync(t => t.RevokedAt == null));

        var result = await h.Auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = GoodPassword,
            NewPassword = "BrandNewPass1"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(0, await h.Db.RefreshTokens.CountAsync(t => t.RevokedAt == null));

        // And the new password is the one that works.
        Assert.True((await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = "BrandNewPass1" })).IsSuccess);
        Assert.True((await h.Auth.LoginAsync(new LoginRequest { UserName = "worker1", Password = GoodPassword })).IsFailure);
    }

    [Fact]
    public async Task Me_returns_roles_and_the_union_of_their_permissions()
    {
        using var h = await ReadyAsync();
        var user = await h.CreateUserAsync(roles: new[] { DefaultRoles.Worker, DefaultRoles.QC });

        var result = await h.Auth.GetCurrentUserAsync(user.Id);

        Assert.True(result.IsSuccess);
        Assert.Contains(Permissions.TaskWork, result.Value!.Permissions);      // from Worker
        Assert.Contains(Permissions.TaskQCReview, result.Value.Permissions);   // from QC
        Assert.DoesNotContain(Permissions.AdminManageUsers, result.Value.Permissions);
    }
}
