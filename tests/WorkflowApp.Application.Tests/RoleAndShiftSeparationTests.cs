using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Identity.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// The three permissions that keep being conflated, and the rules that stop them being.
///
/// <c>Task.Work</c> is "executes tasks". <c>Verification.Work</c> is "investigates whether there is
/// a problem". <c>Workforce.TrackShift</c> is "this person's attendance is measured". None of the
/// three implies either of the others, and every combination is a legitimate configuration —
/// these tests exist because that keeps being assumed away.
/// </summary>
public class RoleAndShiftSeparationTests
{
    [Fact]
    public void Administrator_is_not_a_worker_by_default()
    {
        var administrator = DefaultRoles.Map[DefaultRoles.Administrator];

        // Administering the system says nothing about whether somebody executes tasks or has their
        // hours measured. Granting these by default put a shift widget in front of every
        // administrator, listed them in who-is-working-now, and offered them for real work.
        Assert.DoesNotContain(Permissions.WorkforceTrackShift, administrator);
        Assert.DoesNotContain(Permissions.TaskWork, administrator);
    }

    [Fact]
    public void Administrator_still_holds_every_other_permission_including_the_way_back()
    {
        var administrator = DefaultRoles.Map[DefaultRoles.Administrator];

        // Losing Admin.ManageRoles here would be unrecoverable without SQL: nobody could grant the
        // two removed above back to anyone.
        Assert.Contains(Permissions.AdminManageRoles, administrator);

        var missing = Permissions.All
            .Except(administrator)
            .Except(new[] { Permissions.WorkforceTrackShift, Permissions.TaskWork })
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void An_administrator_who_also_works_is_a_configuration_not_a_special_case()
    {
        // The point of the above: the capability is reachable, it just is not automatic. Anyone
        // holding the Worker role alongside Administrator is on the clock and can execute work.
        var worker = DefaultRoles.Map[DefaultRoles.Worker];

        Assert.Contains(Permissions.WorkforceTrackShift, worker);
        Assert.Contains(Permissions.TaskWork, worker);
    }

    [Fact]
    public void Checking_work_does_not_imply_being_on_the_clock()
    {
        var qc = DefaultRoles.Map[DefaultRoles.QC];

        Assert.Contains(Permissions.VerificationWork, qc);
        Assert.Contains(Permissions.TaskQCReview, qc);

        // Whether a checker's attendance is measured is an independent decision for whoever
        // configures the organisation. The default is that it is not.
        Assert.DoesNotContain(Permissions.WorkforceTrackShift, qc);

        // And investigating is not executing: QC does not get a worker's task permissions.
        Assert.DoesNotContain(Permissions.TaskWork, qc);
    }

    [Fact]
    public void Raising_a_check_and_carrying_one_out_are_different_roles_by_default()
    {
        var reviewer = DefaultRoles.Map[DefaultRoles.Reviewer];
        var qc = DefaultRoles.Map[DefaultRoles.QC];

        // The reviewer routes; the checker investigates. Neither does the other's half.
        Assert.Contains(Permissions.VerificationCreate, reviewer);
        Assert.DoesNotContain(Permissions.VerificationWork, reviewer);

        Assert.Contains(Permissions.VerificationWork, qc);
        Assert.DoesNotContain(Permissions.VerificationCreate, qc);
    }

    [Fact]
    public void Every_permission_in_the_catalogue_is_granted_to_somebody()
    {
        // A permission no default role holds is one a site has to discover for itself. This is not
        // a rule about design so much as a guard against adding a key and forgetting the role map.
        var granted = DefaultRoles.Map.Values.SelectMany(keys => keys).ToHashSet();

        Assert.Empty(Permissions.All.Except(granted));
    }

    [Fact]
    public async Task Signing_in_does_not_start_a_shift()
    {
        // Pinned as a test because it is the invariant most likely to be "helpfully" broken:
        // auth session != shift session != task work session. Somebody may sign in purely to read
        // their requests or a report, and nothing about authenticating declares them at work.
        using var h = await new TestHarness().SeedRolesAndPermissionsAsync();
        var user = await h.CreateUserAsync("wu", roles: DefaultRoles.Worker);

        var signedIn = await h.Auth.LoginAsync(new LoginRequest
        {
            UserName = "wu",
            Password = "CorrectHorse1"
        });

        Assert.True(signedIn.IsSuccess);
        Assert.False(await h.Db.ShiftSessions.AnyAsync(s => s.UserId == user.Id));

        var reloaded = await h.Db.Users.FindAsync(user.Id);
        Assert.NotEqual(WorkforceState.Available, reloaded!.WorkforceState);
        Assert.NotEqual(WorkforceState.Working, reloaded.WorkforceState);
    }
}
