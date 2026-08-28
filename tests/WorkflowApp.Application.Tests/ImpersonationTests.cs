using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Dtos;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Acting as another user, for demonstrating the product and for supporting somebody who cannot
/// describe what they are seeing.
///
/// The whole feature rests on two claims, and these tests are here to keep both of them true.
///
/// It can only ever <em>narrow</em> what the caller can do: the session carries the target's
/// permissions and none of the administrator's, so it is not a way to borrow authority, and a
/// demonstration of a reviewer's screen is an honest one rather than an administrator's screen with
/// a different name on it.
///
/// And it never costs the audit trail its point. Every row records the account the work was done as
/// <em>and</em> the real human behind it, so the trail can say "Faisal, acting: Ahsan" instead of
/// crediting work to somebody who never did it.
/// </summary>
public class ImpersonationTests
{
    private sealed record Fixture(TestHarness H, long AdminId, long WorkerId, long RequesterId);

    private static async Task<Fixture> TeamAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var admin = await h.CreateUserAsync("ahsan", roles: DefaultRoles.Administrator);
        var worker = await h.CreateUserAsync("hanzala", roles: DefaultRoles.Worker);
        var requester = await h.CreateUserAsync("faisal", roles: DefaultRoles.Requester);

        h.ActingAsAdmin(admin.Id);
        return new Fixture(h, admin.Id, worker.Id, requester.Id);
    }

    [Fact]
    public async Task Acting_as_somebody_hands_back_a_session_that_is_theirs()
    {
        var f = await TeamAsync();
        using var _d = f.H;

        var result = await f.H.Auth.ImpersonateAsync(f.AdminId, f.WorkerId);

        Assert.True(result.IsSuccess);
        Assert.Equal("hanzala", result.Value!.User.UserName);
        Assert.Contains(DefaultRoles.Worker, result.Value.User.Roles);
    }

    /// <summary>
    /// The claim that makes this safe to offer at all. If acting-as carried any of the
    /// administrator's own permissions it would be a way to keep them while appearing to be
    /// somebody else, and every screen shown in a demonstration would be a screen nobody else ever
    /// sees.
    /// </summary>
    [Fact]
    public async Task It_carries_the_targets_permissions_and_none_of_the_administrators()
    {
        var f = await TeamAsync();
        using var _d = f.H;

        var result = await f.H.Auth.ImpersonateAsync(f.AdminId, f.RequesterId);
        var permissions = result.Value!.User.Permissions;

        // A requester's own.
        Assert.Contains(Permissions.RequestCreate, permissions);

        // And nothing of the administrator's.
        Assert.DoesNotContain(Permissions.AdminManageUsers, permissions);
        Assert.DoesNotContain(Permissions.AdminImpersonate, permissions);
        Assert.DoesNotContain(Permissions.TaskApprove, permissions);
    }

    /// <summary>
    /// Without this an administrator whose own account was later restricted could step through a
    /// colleague who still holds the permission to get the power back — and the trail would show
    /// the colleague doing it.
    /// </summary>
    [Fact]
    public async Task Somebody_who_can_act_as_others_cannot_be_acted_as()
    {
        var f = await TeamAsync();
        using var _d = f.H;

        var otherAdmin = await f.H.CreateUserAsync("second-admin", roles: DefaultRoles.Administrator);

        var result = await f.H.Auth.ImpersonateAsync(f.AdminId, otherAdmin.Id);

        Assert.Equal("impersonation.target_is_administrator", result.Error!.Code);
    }

    [Fact]
    public async Task A_deactivated_account_cannot_be_acted_as()
    {
        var f = await TeamAsync();
        using var _d = f.H;

        var worker = await f.H.Db.Users.SingleAsync(u => u.Id == f.WorkerId);
        worker.IsActive = false;
        await f.H.Db.SaveChangesAsync();

        var result = await f.H.Auth.ImpersonateAsync(f.AdminId, f.WorkerId);

        Assert.Equal("impersonation.inactive", result.Error!.Code);
    }

    [Fact]
    public async Task Acting_as_yourself_is_refused()
    {
        var f = await TeamAsync();
        using var _d = f.H;

        var result = await f.H.Auth.ImpersonateAsync(f.AdminId, f.AdminId);

        Assert.Equal("impersonation.self", result.Error!.Code);
    }

    [Fact]
    public async Task The_picker_offers_ordinary_people_and_not_other_administrators()
    {
        var f = await TeamAsync();
        using var _d = f.H;

        await f.H.CreateUserAsync("second-admin", roles: DefaultRoles.Administrator);

        var targets = await f.H.Auth.ImpersonationTargetsAsync(f.AdminId);
        var names = targets.Value!.Select(t => t.UserName).ToList();

        Assert.Contains("hanzala", names);
        Assert.Contains("faisal", names);
        Assert.DoesNotContain("second-admin", names);
        Assert.DoesNotContain("ahsan", names);
    }

    /// <summary>
    /// The second claim the feature rests on. Work done while acting is attributed to the account
    /// it was done as — so every existing read of the trail keeps meaning what it meant — with the
    /// real human recorded beside it.
    /// </summary>
    [Fact]
    public async Task Work_done_while_acting_records_both_people()
    {
        var f = await TeamAsync();
        using var _d = f.H;

        // The session an administrator gets after acting as the requester.
        f.H.CurrentUser.UserId = f.RequesterId;
        f.H.CurrentUser.UserName = "faisal";
        f.H.CurrentUser.ImpersonatedByUserId = f.AdminId;
        f.H.CurrentUser.ImpersonatedByUserName = "ahsan";

        f.H.Audit.Record(AuditActions.RequestTriaged, entityType: "Request", entityId: 1);
        await f.H.Db.SaveChangesAsync();

        var row = await f.H.Db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.RequestTriaged);

        Assert.Equal(f.RequesterId, row.ActorUserId);
        Assert.Equal(f.AdminId, row.ImpersonatedByUserId);
    }

    [Fact]
    public async Task An_ordinary_session_records_nobody_behind_it()
    {
        var f = await TeamAsync();
        using var _d = f.H;

        f.H.CurrentUser.UserId = f.RequesterId;
        f.H.CurrentUser.ImpersonatedByUserId = null;

        f.H.Audit.Record(AuditActions.RequestTriaged, entityType: "Request", entityId: 2);
        await f.H.Db.SaveChangesAsync();

        var row = await f.H.Db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.RequestTriaged);

        Assert.Equal(f.RequesterId, row.ActorUserId);
        Assert.Null(row.ImpersonatedByUserId);
    }

    [Fact]
    public async Task Starting_and_stopping_are_both_recorded()
    {
        var f = await TeamAsync();
        using var _d = f.H;

        await f.H.Auth.ImpersonateAsync(f.AdminId, f.WorkerId);

        Assert.True(await f.H.Db.AuditLogs
            .AnyAsync(a => a.Action == AuditActions.ImpersonationStarted && a.ActorUserId == f.AdminId));

        // Now on the acting session, as the client would be.
        f.H.CurrentUser.UserId = f.WorkerId;
        f.H.CurrentUser.UserName = "hanzala";
        f.H.CurrentUser.ImpersonatedByUserId = f.AdminId;

        var back = await f.H.Auth.StopImpersonatingAsync();

        Assert.True(back.IsSuccess);
        Assert.Equal("ahsan", back.Value!.User.UserName);
        Assert.True(await f.H.Db.AuditLogs.AnyAsync(a => a.Action == AuditActions.ImpersonationStopped));
    }

    /// <summary>
    /// The way back is read from the caller's own token, never from anything a client sends, so it
    /// cannot be pointed at somebody else.
    /// </summary>
    [Fact]
    public async Task Stopping_when_not_acting_is_refused()
    {
        var f = await TeamAsync();
        using var _d = f.H;

        f.H.CurrentUser.UserId = f.WorkerId;
        f.H.CurrentUser.ImpersonatedByUserId = null;

        var result = await f.H.Auth.StopImpersonatingAsync();

        Assert.Equal("impersonation.not_acting", result.Error!.Code);
    }

    /// <summary>
    /// Acting-as is a real session against the real database, so work done during it is real work.
    /// That is the point — a demonstration that wrote nowhere would demonstrate nothing — and it is
    /// why the trail has to name the human.
    /// </summary>
    [Fact]
    public async Task Work_done_while_acting_is_real_work_owned_by_the_person_acted_as()
    {
        var f = await TeamAsync();
        using var _d = f.H;

        f.H.CurrentUser.UserId = f.RequesterId;
        f.H.CurrentUser.ImpersonatedByUserId = f.AdminId;
        f.H.CurrentUser.Permissions = new HashSet<string> { Permissions.RequestCreate };

        var request = await f.H.Requests.CreateAsync(f.RequesterId, new CreateRequestDto
        {
            Title = "Raised during a demonstration",
            Description = "Real row, real requester.",
            Type = RequestType.Bug
        });

        Assert.True(request.IsSuccess);

        var stored = await f.H.Db.Requests.AsNoTracking().SingleAsync();
        Assert.Equal(f.RequesterId, stored.RequestedByUserId);
    }
}
