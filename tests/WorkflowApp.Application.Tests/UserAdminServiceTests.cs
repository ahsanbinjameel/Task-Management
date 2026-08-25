using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Identity.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

public class UserAdminServiceTests
{
    private static async Task<TestHarness> ReadyAsync()
    {
        var harness = new TestHarness();
        await harness.SeedRolesAndPermissionsAsync();
        return harness;
    }

    private static CreateUserRequest NewUser(string userName = "alice", params string[] roles) => new()
    {
        UserName = userName,
        Email = $"{userName}@workflowapp.local",
        DisplayName = userName,
        Password = "InitialPass1",
        Roles = roles
    };

    [Fact]
    public async Task Create_user_assigns_roles_and_resolves_permissions()
    {
        using var h = await ReadyAsync();

        var result = await h.UserAdmin.CreateUserAsync(NewUser("alice", DefaultRoles.Reviewer));

        Assert.True(result.IsSuccess);
        Assert.Contains(DefaultRoles.Reviewer, result.Value!.Roles);
        Assert.Contains(Permissions.TaskApprove, result.Value.Permissions);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task Create_user_never_stores_the_password_in_clear()
    {
        using var h = await ReadyAsync();

        await h.UserAdmin.CreateUserAsync(NewUser());

        var stored = await h.Db.Users.FirstAsync();
        Assert.NotEqual("InitialPass1", stored.PasswordHash);
        Assert.Equal(
            Common.Interfaces.PasswordVerification.Success,
            h.PasswordHasher.Verify(stored.PasswordHash, "InitialPass1"));
    }

    [Fact]
    public async Task Create_user_rejects_a_duplicate_username_or_email()
    {
        using var h = await ReadyAsync();
        await h.UserAdmin.CreateUserAsync(NewUser("alice"));

        var duplicateName = await h.UserAdmin.CreateUserAsync(NewUser("alice"));
        Assert.Equal(ErrorType.Conflict, duplicateName.Error!.Type);
        Assert.Equal("user.username_taken", duplicateName.Error.Code);

        var duplicateEmail = await h.UserAdmin.CreateUserAsync(new CreateUserRequest
        {
            UserName = "alice2",
            Email = "alice@workflowapp.local",
            DisplayName = "Alice Two",
            Password = "InitialPass1"
        });
        Assert.Equal("user.email_taken", duplicateEmail.Error!.Code);
    }

    [Fact]
    public async Task Create_user_rejects_an_unknown_role_without_creating_the_user()
    {
        using var h = await ReadyAsync();

        var result = await h.UserAdmin.CreateUserAsync(NewUser("alice", "NotARealRole"));

        Assert.True(result.IsFailure);
        Assert.Equal("role.unknown", result.Error!.Code);
        Assert.False(await h.Db.Users.AnyAsync());
    }

    [Fact]
    public async Task Create_user_enforces_the_password_policy()
    {
        using var h = await ReadyAsync();

        var result = await h.UserAdmin.CreateUserAsync(new CreateUserRequest
        {
            UserName = "alice",
            Email = "alice@workflowapp.local",
            DisplayName = "Alice",
            Password = "alllowercase"
        });

        Assert.True(result.IsFailure);
        Assert.Equal("password.too_weak", result.Error!.Code);
    }

    [Fact]
    public async Task Assign_roles_replaces_the_previous_set()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice", DefaultRoles.Worker));

        var result = await h.UserAdmin.AssignRolesAsync(
            created.Value!.Id, new[] { DefaultRoles.QC, DefaultRoles.Management });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Roles.Count);
        Assert.DoesNotContain(DefaultRoles.Worker, result.Value.Roles);
        Assert.DoesNotContain(Permissions.TaskWork, result.Value.Permissions);
        Assert.Contains(Permissions.TaskQCReview, result.Value.Permissions);
    }

    [Fact]
    public async Task Assign_roles_to_an_empty_set_strips_every_permission()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice", DefaultRoles.Administrator));

        var result = await h.UserAdmin.AssignRolesAsync(created.Value!.Id, Array.Empty<string>());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Roles);
        Assert.Empty(result.Value.Permissions);
    }

    [Fact]
    public async Task Deactivating_a_user_revokes_their_live_sessions_immediately()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice", DefaultRoles.Worker));

        var login = await h.Auth.LoginAsync(new LoginRequest { UserName = "alice", Password = "InitialPass1" });
        Assert.True(login.IsSuccess);

        var deactivate = await h.UserAdmin.SetActiveAsync(created.Value!.Id, false);
        Assert.True(deactivate.IsSuccess);

        // Deactivation must not wait for the access token to expire.
        Assert.Equal(0, await h.Db.RefreshTokens.CountAsync(t => t.RevokedAt == null));

        var refresh = await h.Auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = login.Value!.RefreshToken });
        Assert.True(refresh.IsFailure);

        var reloaded = await h.Db.Users.FirstAsync(u => u.Id == created.Value.Id);
        Assert.Equal(WorkforceState.NotLoggedIn, reloaded.WorkforceState);
    }

    [Fact]
    public async Task Reactivating_a_user_lets_them_log_in_again()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice"));

        await h.UserAdmin.SetActiveAsync(created.Value!.Id, false);
        Assert.True((await h.Auth.LoginAsync(new LoginRequest { UserName = "alice", Password = "InitialPass1" })).IsFailure);

        await h.UserAdmin.SetActiveAsync(created.Value.Id, true);
        Assert.True((await h.Auth.LoginAsync(new LoginRequest { UserName = "alice", Password = "InitialPass1" })).IsSuccess);
    }

    [Fact]
    public async Task Admin_password_reset_clears_lockout_and_revokes_sessions()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice"));

        await h.Auth.LoginAsync(new LoginRequest { UserName = "alice", Password = "InitialPass1" });

        for (var i = 0; i < h.AuthOptions.MaxFailedLoginAttempts; i++)
            await h.Auth.LoginAsync(new LoginRequest { UserName = "alice", Password = "wrong-password" });

        var reset = await h.UserAdmin.ResetPasswordAsync(created.Value!.Id, "ResetPass99");
        Assert.True(reset.IsSuccess);

        var reloaded = await h.Db.Users.FirstAsync(u => u.Id == created.Value.Id);
        Assert.Null(reloaded.LockoutEndAt);
        Assert.Equal(0, reloaded.FailedLoginCount);
        Assert.Equal(0, await h.Db.RefreshTokens.CountAsync(t => t.RevokedAt == null));

        Assert.True((await h.Auth.LoginAsync(new LoginRequest { UserName = "alice", Password = "ResetPass99" })).IsSuccess);
    }

    [Fact]
    public async Task List_users_pages_filters_and_searches()
    {
        using var h = await ReadyAsync();
        for (var i = 1; i <= 7; i++)
            await h.UserAdmin.CreateUserAsync(NewUser($"user{i}"));

        await h.UserAdmin.SetActiveAsync((await h.Db.Users.FirstAsync(u => u.UserName == "user3")).Id, false);

        var firstPage = await h.UserAdmin.ListUsersAsync(new PageQuery { Page = 1, PageSize = 5 });
        Assert.Equal(5, firstPage.Items.Count);
        Assert.Equal(7, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);

        var activeOnly = await h.UserAdmin.ListUsersAsync(new PageQuery(), isActive: true);
        Assert.Equal(6, activeOnly.TotalCount);

        var searched = await h.UserAdmin.ListUsersAsync(new PageQuery(), search: "user3");
        Assert.Single(searched.Items);
    }

    [Fact]
    public void Page_query_clamps_hostile_values()
    {
        var query = new PageQuery { Page = -4, PageSize = 100_000 };

        Assert.Equal(1, query.NormalizedPage);
        Assert.Equal(200, query.NormalizedPageSize);
        Assert.Equal(0, query.Skip);
    }

    [Fact]
    public async Task List_roles_reports_the_seeded_permission_grants()
    {
        using var h = await ReadyAsync();

        var roles = await h.UserAdmin.ListRolesAsync();

        var administrator = Assert.Single(roles, r => r.Name == DefaultRoles.Administrator);
        Assert.Equal(Permissions.All.Length, administrator.Permissions.Count);

        var worker = Assert.Single(roles, r => r.Name == DefaultRoles.Worker);
        // Workers execute tasks and are the only default role on the clock.
        Assert.Contains(Permissions.TaskWork, worker.Permissions);
        Assert.Contains(Permissions.WorkforceTrackShift, worker.Permissions);

        // Nobody else is shift-tracked by default.
        foreach (var role in roles.Where(r =>
                     r.Name != DefaultRoles.Worker && r.Name != DefaultRoles.Administrator))
        {
            Assert.DoesNotContain(Permissions.WorkforceTrackShift, role.Permissions);
        }
    }

    // --- editing an account ------------------------------------------------------------------

    [Fact]
    public async Task Updating_a_user_changes_the_name_and_email()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice"));

        var result = await h.UserAdmin.UpdateUserAsync(created.Value!.Id, new UpdateUserRequest
        {
            UserName = "alice",
            DisplayName = "Alice Okonkwo",
            Email = "alice.okonkwo@workflowapp.local",
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Alice Okonkwo", result.Value!.DisplayName);
        Assert.Equal("alice.okonkwo@workflowapp.local", result.Value!.Email);
    }

    /// <summary>
    /// The username is editable. Every back-reference is the numeric id, so the audit trail follows
    /// the rename rather than being orphaned by it.
    /// </summary>
    [Fact]
    public async Task The_username_can_be_changed()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice"));

        var result = await h.UserAdmin.UpdateUserAsync(created.Value!.Id,
            new UpdateUserRequest { UserName = "alice.o", DisplayName = "Alice" });

        Assert.True(result.IsSuccess);
        Assert.Equal("alice.o", result.Value!.UserName);
    }

    [Fact]
    public async Task A_username_already_taken_is_refused()
    {
        using var h = await ReadyAsync();
        await h.UserAdmin.CreateUserAsync(NewUser("alice"));
        var bob = await h.UserAdmin.CreateUserAsync(NewUser("bob"));

        var result = await h.UserAdmin.UpdateUserAsync(bob.Value!.Id,
            new UpdateUserRequest { UserName = "ALICE", DisplayName = "Bob" });

        Assert.True(result.IsFailure);
        Assert.Equal("user.duplicate_username", result.Error!.Code);
    }

    /// <summary>
    /// A blank password leaves the existing one working. An administrator fixing a surname must not
    /// have to reissue credentials to do it.
    /// </summary>
    [Fact]
    public async Task Leaving_the_password_blank_keeps_the_current_one()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice"));

        await h.UserAdmin.UpdateUserAsync(created.Value!.Id,
            new UpdateUserRequest { UserName = "alice", DisplayName = "Alice Renamed" });

        var signIn = await h.Auth.LoginAsync(new LoginRequest
        {
            UserName = "alice",
            Password = "InitialPass1",
        });

        Assert.True(signIn.IsSuccess);
    }

    [Fact]
    public async Task Setting_a_password_replaces_it_and_ends_live_sessions()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice"));
        var first = await h.Auth.LoginAsync(
            new LoginRequest { UserName = "alice", Password = "InitialPass1" });
        Assert.True(first.IsSuccess);

        var result = await h.UserAdmin.UpdateUserAsync(created.Value!.Id, new UpdateUserRequest
        {
            UserName = "alice",
            DisplayName = "Alice",
            NewPassword = "BrandNewPass9",
        });

        Assert.True(result.IsSuccess);

        var oldPassword = await h.Auth.LoginAsync(
            new LoginRequest { UserName = "alice", Password = "InitialPass1" });
        Assert.True(oldPassword.IsFailure);

        var newPassword = await h.Auth.LoginAsync(
            new LoginRequest { UserName = "alice", Password = "BrandNewPass9" });
        Assert.True(newPassword.IsSuccess);
    }

    [Fact]
    public async Task A_weak_new_password_is_refused_and_nothing_else_is_saved()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice"));

        var result = await h.UserAdmin.UpdateUserAsync(created.Value!.Id, new UpdateUserRequest
        {
            UserName = "alice",
            DisplayName = "Should Not Stick",
            NewPassword = "short",
        });

        Assert.True(result.IsFailure);

        var after = await h.UserAdmin.GetUserAsync(created.Value!.Id);
        Assert.NotEqual("Should Not Stick", after.Value!.DisplayName);
    }

    // --- self service ------------------------------------------------------------------------

    [Fact]
    public async Task A_person_can_change_their_own_name_and_email()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice"));

        var result = await h.UserAdmin.UpdateProfileAsync(created.Value!.Id, new UpdateProfileRequest
        {
            DisplayName = "Alice O.",
            Email = "alice.o@workflowapp.local",
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Alice O.", result.Value!.DisplayName);
    }

    /// <summary>Self-service cannot reach the username: that stays an administrator's to set.</summary>
    [Fact]
    public async Task Changing_your_own_profile_leaves_the_username_alone()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice"));

        await h.UserAdmin.UpdateProfileAsync(created.Value!.Id,
            new UpdateProfileRequest { DisplayName = "Alice O." });

        var after = await h.UserAdmin.GetUserAsync(created.Value!.Id);
        Assert.Equal("alice", after.Value!.UserName);
    }

    [Fact]
    public async Task An_email_already_used_by_another_account_is_refused()
    {
        using var h = await ReadyAsync();
        await h.UserAdmin.CreateUserAsync(NewUser("alice"));
        var bob = await h.UserAdmin.CreateUserAsync(NewUser("bob"));

        var result = await h.UserAdmin.UpdateUserAsync(bob.Value!.Id, new UpdateUserRequest
        {
            UserName = "bob",
            DisplayName = "Bob",
            Email = "alice@workflowapp.local",
        });

        Assert.True(result.IsFailure);
        Assert.Equal("user.duplicate_email", result.Error!.Code);
    }

    [Fact]
    public async Task Clearing_the_email_is_allowed()
    {
        using var h = await ReadyAsync();
        var created = await h.UserAdmin.CreateUserAsync(NewUser("alice"));

        var result = await h.UserAdmin.UpdateUserAsync(created.Value!.Id,
            new UpdateUserRequest { UserName = "alice", DisplayName = "Alice", Email = "   " });

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Email);
    }
}
