using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// A parent task cannot be finished while the work it was broken into is still outstanding.
///
/// The rule lives in the service, not the screen. A disabled button is a courtesy — the endpoint is
/// reachable directly, and a page left open while somebody reopens a subtask would otherwise let a
/// parent through. Optional subtasks are the deliberate exception.
/// </summary>
public class RequiredSubtaskTests
{
    private sealed record Fixture(TestHarness H, long ParentId, long WorkerId, long CoordinatorId);

    private static async Task<Fixture> ParentAssignedToWorkerAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("rachel");
        var reviewer = await h.CreateUserAsync("victor");
        var coordinator = await h.CreateUserAsync("amara");
        var worker = await h.CreateUserAsync("wu", roles: new[] { "Worker" });

        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Payroll changes",
            Description = "Several pieces of work.",
            Type = RequestType.ChangeRequest
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.High
        });

        var parent = await h.Db.Tasks.SingleAsync();
        h.ActingAsAdmin(coordinator.Id);

        await h.Assignment.AssignAsync(parent.Id, coordinator.Id,
            new AssignTaskDto { AssigneeUserId = worker.Id });

        await h.StartShiftAsync(worker.Id);
        await h.WorkSessions.StartAsync(parent.Id, worker.Id);

        return new Fixture(h, parent.Id, worker.Id, coordinator.Id);
    }

    [Fact]
    public async Task A_parent_cannot_be_finished_while_a_required_subtask_is_open()
    {
        var f = await ParentAssignedToWorkerAsync();
        using var _d = f.H;

        await f.H.TaskCreation.CreateSubtaskAsync(f.ParentId, f.CoordinatorId,
            new CreateSubtaskDto { Title = "Database changes", Description = "Schema." });
        await f.H.TaskCreation.CreateSubtaskAsync(f.ParentId, f.CoordinatorId,
            new CreateSubtaskDto { Title = "API changes", Description = "Endpoints." });

        var refused = await f.H.WorkSessions.CompleteAsync(f.ParentId, f.WorkerId, "done?");

        Assert.True(refused.IsFailure);
        Assert.Equal("task.required_subtasks_open", refused.Error!.Code);

        // The message has to say what is in the way, in words a non-technical reader understands.
        Assert.Contains("2 smaller tasks", refused.Error!.Message);
        Assert.DoesNotContain("Exception", refused.Error!.Message);
    }

    [Fact]
    public async Task An_optional_subtask_does_not_hold_the_parent_up()
    {
        var f = await ParentAssignedToWorkerAsync();
        using var _d = f.H;

        await f.H.TaskCreation.CreateSubtaskAsync(f.ParentId, f.CoordinatorId,
            new CreateSubtaskDto
            {
                Title = "Nice-to-have tidy up",
                Description = "Can follow later.",
                IsRequired = false
            });

        var completed = await f.H.WorkSessions.CompleteAsync(f.ParentId, f.WorkerId, "done");

        Assert.True(completed.IsSuccess);
        Assert.Equal(WorkTaskStatus.CompletedReadyForQC, completed.Value!.Status);
    }

    [Fact]
    public async Task Finishing_the_required_subtasks_releases_the_parent()
    {
        var f = await ParentAssignedToWorkerAsync();
        using var _d = f.H;

        var sub = await f.H.TaskCreation.CreateSubtaskAsync(f.ParentId, f.CoordinatorId,
            new CreateSubtaskDto { Title = "Database changes", Description = "Schema." });

        var blocked = await f.H.WorkSessions.CompleteAsync(f.ParentId, f.WorkerId, "done?");
        Assert.Equal("task.required_subtasks_open", blocked.Error!.Code);

        // Cancel it — a terminal state counts as finished with, not merely closed.
        var subtask = await f.H.Db.Tasks.FirstAsync(t => t.Id == sub.Value!.Id);
        subtask.Status = WorkTaskStatus.Cancelled;
        await f.H.Db.SaveChangesAsync();

        var completed = await f.H.WorkSessions.CompleteAsync(f.ParentId, f.WorkerId, "done");
        Assert.True(completed.IsSuccess);
    }

    [Fact]
    public async Task Subtasks_are_visible_on_the_parent_with_who_is_responsible()
    {
        var f = await ParentAssignedToWorkerAsync();
        using var _d = f.H;

        await f.H.TaskCreation.CreateSubtaskAsync(f.ParentId, f.CoordinatorId,
            new CreateSubtaskDto
            {
                Title = "Database changes",
                Description = "Schema.",
                AssigneeUserId = f.WorkerId
            });
        await f.H.TaskCreation.CreateSubtaskAsync(f.ParentId, f.CoordinatorId,
            new CreateSubtaskDto { Title = "Optional tidy", Description = "Later.", IsRequired = false });

        var parent = await f.H.TaskQueries.GetAsync(f.ParentId);
        var subs = parent.Value!.SubTasks;

        Assert.Equal(2, subs.Count);

        // Required first, so what actually blocks the parent reads at the top.
        Assert.True(subs[0].IsRequired);
        Assert.False(subs[1].IsRequired);

        Assert.Equal("wu", subs[0].ResponsiblePersonName);
        Assert.Null(subs[1].ResponsiblePersonName);
    }
}
