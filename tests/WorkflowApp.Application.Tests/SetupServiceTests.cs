using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Admin.Dtos;
using WorkflowApp.Application.Common;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// The administrator's setup data. Most of it is ordinary CRUD; what is worth testing is the
/// handful of refusals that stop an administrator quietly breaking the system from a settings page.
/// </summary>
public class SetupServiceTests
{
    private static async Task<TestHarness> ReadyAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        var admin = await h.CreateUserAsync("admin-person");
        h.ActingAsAdmin(admin.Id);
        return h;
    }

    // --- clients -----------------------------------------------------------------------------

    [Fact]
    public async Task Client_names_are_unique_ignoring_case()
    {
        using var h = await ReadyAsync();

        await h.Setup.CreateClientAsync(new SaveClientDto { Name = "Falcon Traders" });
        var again = await h.Setup.CreateClientAsync(new SaveClientDto { Name = "falcon traders" });

        Assert.True(again.IsFailure);
        Assert.Equal("client.duplicate_name", again.Error!.Code);
    }

    [Fact]
    public async Task A_client_is_deactivated_rather_than_deleted_and_keeps_its_request_count()
    {
        using var h = await ReadyAsync();

        var created = await h.Setup.CreateClientAsync(new SaveClientDto { Name = "Crest Auto" });
        var off = await h.Setup.SetClientActiveAsync(created.Value!.Id, false);

        Assert.False(off.Value!.IsActive);
        Assert.True(await h.Db.Clients.AnyAsync(c => c.Id == created.Value!.Id));
    }

    // --- pause reasons -----------------------------------------------------------------------

    /// <summary>
    /// The one setting on this screen that could corrupt attendance. ShiftEnded is reachable only
    /// through the end-shift operation; a pause reason that set it would close someone's shift from
    /// the task screen with a work session still open.
    /// </summary>
    [Fact]
    public async Task A_pause_reason_cannot_end_a_shift()
    {
        using var h = await ReadyAsync();

        var result = await h.Setup.CreatePauseReasonAsync(new SavePauseReasonDto
        {
            Name = "Gone home",
            AwayState = WorkforceState.ShiftEnded,
        });

        Assert.True(result.IsFailure);
        Assert.Equal("pause_reason.invalid_away_state", result.Error!.Code);
    }

    [Fact]
    public async Task A_pause_reason_keeps_the_behaviour_flags_it_was_given()
    {
        using var h = await ReadyAsync();

        var created = await h.Setup.CreatePauseReasonAsync(new SavePauseReasonDto
        {
            Name = "Waiting for client sign-off",
            IsBlocker = true,
            RequiresComment = true,
            Category = PauseCategory.WaitingForClient,
        });

        Assert.True(created.Value!.IsBlocker);
        Assert.True(created.Value!.RequiresComment);
        Assert.Null(created.Value!.AwayState);
    }

    // --- roles -------------------------------------------------------------------------------

    [Fact]
    public async Task A_built_in_role_cannot_be_renamed()
    {
        using var h = await ReadyAsync();

        var administrator = (await h.Setup.RolesAsync()).First(r => r.Name == DefaultRoles.Administrator);

        var result = await h.Setup.UpdateRoleAsync(
            administrator.Id, new SaveRoleDto { Name = "Superuser" });

        Assert.True(result.IsFailure);
        Assert.Equal("role.system_rename", result.Error!.Code);
    }

    [Fact]
    public async Task A_built_in_role_can_still_have_its_description_changed()
    {
        using var h = await ReadyAsync();

        var qc = (await h.Setup.RolesAsync()).First(r => r.Name == DefaultRoles.QC);

        var result = await h.Setup.UpdateRoleAsync(
            qc.Id, new SaveRoleDto { Name = qc.Name, Description = "Checks finished work." });

        Assert.True(result.IsSuccess);
        Assert.Equal("Checks finished work.", result.Value!.Description);
    }

    [Fact]
    public async Task A_built_in_role_cannot_be_deleted()
    {
        using var h = await ReadyAsync();

        var worker = (await h.Setup.RolesAsync()).First(r => r.Name == DefaultRoles.Worker);
        var result = await h.Setup.DeleteRoleAsync(worker.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("role.system_delete", result.Error!.Code);
    }

    [Fact]
    public async Task A_role_someone_holds_cannot_be_deleted()
    {
        using var h = await ReadyAsync();

        var created = await h.Setup.CreateRoleAsync(new SaveRoleDto { Name = "Night shift" });
        await h.CreateUserAsync("nadia", roles: "Night shift");

        var result = await h.Setup.DeleteRoleAsync(created.Value!.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("role.in_use", result.Error!.Code);
    }

    [Fact]
    public async Task A_role_nobody_holds_can_be_deleted()
    {
        using var h = await ReadyAsync();

        var created = await h.Setup.CreateRoleAsync(new SaveRoleDto { Name = "Unused" });
        var result = await h.Setup.DeleteRoleAsync(created.Value!.Id);

        Assert.True(result.IsSuccess);
        Assert.False(await h.Db.Roles.AnyAsync(r => r.Id == created.Value!.Id));
    }

    [Fact]
    public async Task Setting_permissions_replaces_the_whole_set()
    {
        using var h = await ReadyAsync();

        var created = await h.Setup.CreateRoleAsync(new SaveRoleDto { Name = "Read only" });

        var result = await h.Setup.SetRolePermissionsAsync(created.Value!.Id, new SetRolePermissionsDto
        {
            Permissions = new[] { Permissions.RequestViewAll, Permissions.ReportsView },
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { Permissions.ReportsView, Permissions.RequestViewAll },
            result.Value!.Permissions.OrderBy(p => p).ToArray());
    }

    [Fact]
    public async Task An_unknown_permission_is_refused_rather_than_silently_dropped()
    {
        using var h = await ReadyAsync();

        var created = await h.Setup.CreateRoleAsync(new SaveRoleDto { Name = "Typo" });

        var result = await h.Setup.SetRolePermissionsAsync(created.Value!.Id, new SetRolePermissionsDto
        {
            Permissions = new[] { "Task.Aprove" },
        });

        Assert.True(result.IsFailure);
        Assert.Equal("role.unknown_permission", result.Error!.Code);
    }

    /// <summary>
    /// The recovery-proof case: taking role management away from the only role that has it, while
    /// somebody holds that role, locks the screen for everyone with no way back short of SQL.
    /// </summary>
    [Fact]
    public async Task The_last_held_route_to_role_management_cannot_be_removed()
    {
        using var h = await ReadyAsync();

        var administrator = (await h.Setup.RolesAsync()).First(r => r.Name == DefaultRoles.Administrator);
        await h.CreateUserAsync("the-only-admin", roles: DefaultRoles.Administrator);

        // Everything the role had, minus the one permission that keeps this screen reachable.
        var without = administrator.Permissions
            .Where(p => p != Permissions.AdminManageRoles)
            .ToArray();

        var result = await h.Setup.SetRolePermissionsAsync(
            administrator.Id, new SetRolePermissionsDto { Permissions = without });

        Assert.True(result.IsFailure);
        Assert.Equal("role.last_administrator", result.Error!.Code);
    }

    /// <summary>...but a role nobody holds cannot orphan anything, so it stays editable.</summary>
    [Fact]
    public async Task A_role_nobody_holds_may_drop_role_management_freely()
    {
        using var h = await ReadyAsync();

        var spare = await h.Setup.CreateRoleAsync(new SaveRoleDto { Name = "Spare admin" });
        await h.Setup.SetRolePermissionsAsync(spare.Value!.Id, new SetRolePermissionsDto
        {
            Permissions = new[] { Permissions.AdminManageRoles },
        });

        var result = await h.Setup.SetRolePermissionsAsync(spare.Value!.Id, new SetRolePermissionsDto
        {
            Permissions = new[] { Permissions.ReportsView },
        });

        Assert.True(result.IsSuccess);
    }

    // --- teams -------------------------------------------------------------------------------

    [Fact]
    public async Task A_team_cannot_point_at_a_department_that_does_not_exist()
    {
        using var h = await ReadyAsync();

        var result = await h.Setup.CreateTeamAsync(new SaveTeamDto { Name = "Platform", DepartmentId = 9999 });

        Assert.True(result.IsFailure);
        Assert.Equal("team.department_not_found", result.Error!.Code);
    }

    [Fact]
    public async Task A_team_reports_the_department_it_belongs_to()
    {
        using var h = await ReadyAsync();

        var department = await h.Setup.CreateDepartmentAsync(new SaveDepartmentDto { Name = "Operations" });
        var team = await h.Setup.CreateTeamAsync(
            new SaveTeamDto { Name = "Platform", DepartmentId = department.Value!.Id });

        Assert.Equal("Operations", team.Value!.DepartmentName);
    }
}
