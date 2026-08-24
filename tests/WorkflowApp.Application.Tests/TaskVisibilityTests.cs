using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Who may read a task, and how much of it they are handed.
///
/// The task *list* was scoped to work someone is part of, but the *detail* endpoint took an id and
/// answered. A lock on the door of an unwalled room: anyone signed in could walk a URL through
/// every task in the system. These tests pin the wall in place, and pin the second half of the
/// rule too — that a requester following their own work through to the task gets what it is and
/// how far along it is, not the estimate, the sitting-by-sitting timings, the reassignment trail
/// or what a checker wrote about a colleague's work.
/// </summary>
public class TaskVisibilityTests
{
    private sealed record Fixture(
        TestHarness H, long TaskId, long RequesterId, long OwnerId, long HelperId,
        long BystanderId, long CoordinatorId);

    private static readonly string[] WorkerRole = { "Worker" };

    /// <summary>A task with a requester, an owner, a helper and someone with nothing to do with it.</summary>
    private static async Task<Fixture> TaskAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("rachel");
        var reviewer = await h.CreateUserAsync("victor");
        var coordinator = await h.CreateUserAsync("amara");
        var owner = await h.CreateUserAsync("wu", roles: WorkerRole);
        var helper = await h.CreateUserAsync("priya", roles: WorkerRole);
        var bystander = await h.CreateUserAsync("morgan", roles: WorkerRole);

        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Invoice totals are wrong",
            Description = "The VAT line double-counts.",
            Type = RequestType.Bug,
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.High,
            EstimatedEffortHours = 6m,
        });

        var task = await h.Db.Tasks.SingleAsync();

        h.ActingAsAdmin(coordinator.Id);
        await h.Assignment.AssignAsync(task.Id, coordinator.Id,
            new AssignTaskDto { AssigneeUserId = owner.Id });
        await h.Assignment.AddCollaboratorAsync(task.Id, helper.Id, coordinator.Id);

        return new Fixture(h, task.Id, requester.Id, owner.Id, helper.Id, bystander.Id, coordinator.Id);
    }

    [Fact]
    public async Task GetAsync_responsible_person_sees_the_task()
    {
        var f = await TaskAsync();
        f.H.ActingAs(f.OwnerId, Permissions.TaskWork, Permissions.WorkforceTrackShift);

        var result = await f.H.TaskQueries.GetAsync(f.TaskId);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetAsync_support_person_sees_the_task()
    {
        var f = await TaskAsync();
        f.H.ActingAs(f.HelperId, Permissions.TaskWork, Permissions.WorkforceTrackShift);

        var result = await f.H.TaskQueries.GetAsync(f.TaskId);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetAsync_requester_sees_the_task_their_request_became()
    {
        var f = await TaskAsync();
        f.H.ActingAs(f.RequesterId, Permissions.RequestCreate, Permissions.RequestViewOwn);

        var result = await f.H.TaskQueries.GetAsync(f.TaskId);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetAsync_unrelated_worker_is_refused()
    {
        var f = await TaskAsync();
        f.H.ActingAs(f.BystanderId, Permissions.TaskWork, Permissions.WorkforceTrackShift);

        var result = await f.H.TaskQueries.GetAsync(f.TaskId);

        Assert.False(result.IsSuccess);

        // Not Found, not Forbidden: "you may not see this" still confirms it exists.
        Assert.Equal("task.not_found", result.Error!.Code);
    }

    [Fact]
    public async Task GetAsync_coordinator_sees_any_task()
    {
        var f = await TaskAsync();
        f.H.ActingAs(f.CoordinatorId, Permissions.TaskAssign);

        var result = await f.H.TaskQueries.GetAsync(f.TaskId);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetAsync_requester_is_not_given_the_internal_record()
    {
        var f = await TaskAsync();

        // The owner works on it for a while, so there is something to hide.
        f.H.ActingAs(f.OwnerId, Permissions.TaskWork, Permissions.WorkforceTrackShift);
        await f.H.StartShiftAsync(f.OwnerId);
        await f.H.WorkSessions.StartAsync(f.TaskId, f.OwnerId);
        f.H.Clock.Advance(TimeSpan.FromHours(2));
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.OwnerId, "Fixed.");

        f.H.ActingAs(f.RequesterId, Permissions.RequestCreate, Permissions.RequestViewOwn);
        var asRequester = (await f.H.TaskQueries.GetAsync(f.TaskId)).Value!;

        Assert.Empty(asRequester.WorkSessions);
        Assert.Empty(asRequester.AssignmentHistory);
        Assert.Empty(asRequester.StatusHistory);
        Assert.Empty(asRequester.Activity);
        Assert.Null(asRequester.EstimatedEffortHours);
        Assert.Equal(TimeSpan.Zero, asRequester.TotalWorkedTime);

        // What they came for is still there.
        Assert.Equal("Invoice totals are wrong", asRequester.Title);
        Assert.Equal(WorkTaskStatus.CompletedReadyForQC, asRequester.Status);
        Assert.NotNull(asRequester.PrimaryAssigneeDisplayName);

        // And the same task, read by someone inside the process, is whole.
        f.H.ActingAs(f.CoordinatorId, Permissions.TaskAssign);
        var asCoordinator = (await f.H.TaskQueries.GetAsync(f.TaskId)).Value!;

        Assert.NotEmpty(asCoordinator.WorkSessions);
        Assert.NotEmpty(asCoordinator.StatusHistory);
        Assert.Equal(6m, asCoordinator.EstimatedEffortHours);
        Assert.True(asCoordinator.TotalWorkedTime > TimeSpan.Zero);
    }
}
