using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Application.Common;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// PRODUCT-CORE §12C: the assignment screen shows facts, not a fabricated capacity number.
///
/// The panel this replaces summed estimated effort and called the total "capacity". Most tasks
/// carry no estimate at all, the ones that do carry a guess, and a sum of guesses is not something
/// a coordinator can act on. What they actually ask is who is here, what they are on this minute,
/// what is already queued behind it, and whether they have touched this part of the product before.
/// </summary>
public class AssignmentCandidateTests
{
    private sealed record Fixture(TestHarness H, long TaskId, long CoordinatorId);

    /// <summary>An approved, unassigned task for a client, plus three people who could do it.</summary>
    private static async Task<Fixture> ReadyToAssignAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("faisal");
        var coordinator = await h.CreateUserAsync("ahsan");
        await h.CreateUserAsync("hanzala", roles: DefaultRoles.Worker);
        await h.CreateUserAsync("uzair", roles: DefaultRoles.Worker);
        await h.CreateUserAsync("umer", roles: DefaultRoles.Worker);

        h.ActingAsAdmin(coordinator.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Delivery Order detail report total is wrong",
            Description = "The total row does not match the sum of the lines.",
            Type = RequestType.Bug,
            ClientName = "Impression Sourcing"
        });

        await h.Triage.DecideAsync(request.Value!.Id, coordinator.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.Normal
        });

        var task = await h.Db.Tasks.SingleAsync();
        return new Fixture(h, task.Id, coordinator.Id);
    }

    /// <summary>
    /// The bug that made the old panel useless for its actual job. It was built from people who
    /// already had open tasks, so anyone free never appeared — and "who is free" is most of the
    /// question a coordinator is asking.
    /// </summary>
    [Fact]
    public async Task People_with_nothing_on_are_still_offered()
    {
        var f = await ReadyToAssignAsync();
        using var _d = f.H;

        var result = await f.H.TaskQueries.AssignmentCandidatesAsync(f.TaskId);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.All(result.Value, c => Assert.Equal(0, c.ActiveCount + c.WaitingCount));
    }

    [Fact]
    public async Task A_candidate_reports_what_they_are_working_on_right_now()
    {
        var f = await ReadyToAssignAsync();
        using var _d = f.H;

        var worker = await f.H.Db.Users.SingleAsync(u => u.UserName == "hanzala");

        // Give them something else and start it, so there is a running timer to report.
        var other = await f.H.Db.Tasks.SingleAsync();
        await f.H.Assignment.AssignAsync(other.Id, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = worker.Id });

        await f.H.StartShiftAsync(worker.Id);
        f.H.ActingAsAdmin(worker.Id);
        await f.H.WorkSessions.StartAsync(other.Id, worker.Id);
        f.H.Clock.Advance(TimeSpan.FromMinutes(72));

        f.H.ActingAsAdmin(f.CoordinatorId);
        var result = await f.H.TaskQueries.AssignmentCandidatesAsync(f.TaskId);

        var busy = result.Value!.Single(c => c.UserId == worker.Id);

        Assert.True(busy.IsOnShift);
        Assert.Equal(other.TaskNumber, busy.ActiveTaskNumber);
        Assert.Equal(TimeSpan.FromMinutes(72), busy.ActiveFor);
        Assert.Equal(1, busy.ActiveCount);
    }

    [Fact]
    public async Task Someone_who_is_not_on_shift_says_so()
    {
        var f = await ReadyToAssignAsync();
        using var _d = f.H;

        var result = await f.H.TaskQueries.AssignmentCandidatesAsync(f.TaskId);

        // Nobody has started a shift in this fixture, so nobody is on the clock.
        Assert.All(result.Value!, c => Assert.False(c.IsOnShift));
        Assert.All(result.Value!, c => Assert.Null(c.ActiveTaskNumber));
    }

    [Fact]
    public async Task Work_already_theirs_but_not_started_counts_as_waiting()
    {
        var f = await ReadyToAssignAsync();
        using var _d = f.H;

        var worker = await f.H.Db.Users.SingleAsync(u => u.UserName == "uzair");
        var task = await f.H.Db.Tasks.SingleAsync();

        await f.H.Assignment.AssignAsync(task.Id, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = worker.Id });

        var result = await f.H.TaskQueries.AssignmentCandidatesAsync(f.TaskId);
        var them = result.Value!.Single(c => c.UserId == worker.Id);

        Assert.Equal(0, them.ActiveCount);
        Assert.Equal(1, them.WaitingCount);
    }

    /// <summary>
    /// The one fact on this panel that is about fit rather than availability: has this person seen
    /// this part of the product before.
    /// </summary>
    [Fact]
    public async Task Recent_work_on_the_same_client_is_surfaced()
    {
        var f = await ReadyToAssignAsync();
        using var _d = f.H;

        var worker = await f.H.Db.Users.SingleAsync(u => u.UserName == "umer");
        var task = await f.H.Db.Tasks.SingleAsync();

        // A second task for the same client, already theirs.
        f.H.Db.Tasks.Add(new WorkTask
        {
            TaskNumber = "TSK-000900",
            Title = "Delivery Order master report column",
            Description = "Earlier work on the same client.",
            ClientId = task.ClientId,
            Status = WorkTaskStatus.Closed,
            PrimaryAssigneeUserId = worker.Id,
        });
        await f.H.Db.SaveChangesAsync();

        var result = await f.H.TaskQueries.AssignmentCandidatesAsync(f.TaskId);

        var them = result.Value!.Single(c => c.UserId == worker.Id);
        Assert.Contains("Delivery Order master report column", them.RecentRelated);

        // And it is not attributed to everybody else.
        var others = result.Value!.Where(c => c.UserId != worker.Id);
        Assert.All(others, c => Assert.Empty(c.RecentRelated));
    }

    [Fact]
    public async Task Candidates_for_a_task_that_does_not_exist_are_a_not_found()
    {
        var f = await ReadyToAssignAsync();
        using var _d = f.H;

        var result = await f.H.TaskQueries.AssignmentCandidatesAsync(999_999);

        Assert.Equal("task.not_found", result.Error!.Code);
    }
}
