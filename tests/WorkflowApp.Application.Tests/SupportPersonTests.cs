using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Support Person is a different relationship from Responsible Person, and the difference has to
/// hold everywhere the system counts work — not just on the screen that displays it.
///
/// These tests pin the separation down: helping with a task must never put it in someone's queue,
/// raise their task count, add to their workload, or appear in their report as work they own. They
/// exist because that distinction is easy to break accidentally — one `Union` in a queue query
/// would do it, and nothing else in the suite would notice.
/// </summary>
public class SupportPersonTests
{
    private sealed record Fixture(
        TestHarness H, long TaskId, long OwnerId, long HelperId, long CoordinatorId);

    private static async Task<Fixture> TaskWithHelperAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("rachel");
        var reviewer = await h.CreateUserAsync("victor");
        var coordinator = await h.CreateUserAsync("amara");
        var owner = await h.CreateUserAsync("ahsan", roles: DefaultRolesWorker);
        var helper = await h.CreateUserAsync("hunzala", roles: DefaultRolesWorker);

        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Payroll changes",
            Description = "Tax bands need updating.",
            Type = RequestType.ChangeRequest
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.High,
        });

        var task = await h.Db.Tasks.SingleAsync();
        h.ActingAsAdmin(coordinator.Id);

        // Ahsan is responsible; Hunzala only helps.
        await h.Assignment.AssignAsync(task.Id, coordinator.Id,
            new AssignTaskDto { AssigneeUserId = owner.Id });
        await h.Assignment.AddCollaboratorAsync(task.Id, helper.Id, coordinator.Id);

        return new Fixture(h, task.Id, owner.Id, helper.Id, coordinator.Id);
    }

    private static readonly string[] DefaultRolesWorker = { "Worker" };

    [Fact]
    public async Task Helping_with_a_task_does_not_put_it_in_the_helpers_queue()
    {
        var f = await TaskWithHelperAsync();
        using var _d = f.H;

        var ownerQueue = await f.H.TaskQueries.MyQueueAsync(f.OwnerId);
        var helperQueue = await f.H.TaskQueries.MyQueueAsync(f.HelperId);

        Assert.Contains(ownerQueue, t => t.Id == f.TaskId);
        Assert.Empty(helperQueue);
    }

    [Fact]
    public async Task Helping_with_a_task_does_not_count_towards_the_helpers_workload()
    {
        var f = await TaskWithHelperAsync();
        using var _d = f.H;

        var workload = await f.H.TaskQueries.WorkloadAsync();

        var owner = Assert.Single(workload, w => w.UserId == f.OwnerId);
        Assert.Equal(1, owner.OpenTaskCount);

        // The helper should not appear at all: workload is built from ownership, and they own none.
        Assert.DoesNotContain(workload, w => w.UserId == f.HelperId);
    }

    [Fact]
    public async Task The_support_person_is_still_visible_on_the_task()
    {
        var f = await TaskWithHelperAsync();
        using var _d = f.H;

        var task = await f.H.TaskQueries.GetAsync(f.TaskId);

        var support = Assert.Single(task.Value!.SupportPeople);
        Assert.Equal(f.HelperId, support.UserId);
        Assert.Equal("hunzala", support.DisplayName);

        // ...and is not confused with the person responsible.
        Assert.Equal(f.OwnerId, task.Value!.PrimaryAssigneeUserId);
    }

    [Fact]
    public async Task The_same_person_cannot_be_responsible_and_helping_at_once()
    {
        var f = await TaskWithHelperAsync();
        using var _d = f.H;

        // Adding the responsible person as a helper is refused...
        var refused = await f.H.Assignment.AddCollaboratorAsync(f.TaskId, f.OwnerId, f.CoordinatorId);
        Assert.True(refused.IsFailure);
        Assert.Equal("task.assignee_is_not_collaborator", refused.Error!.Code);

        // ...and handing the task to someone who was helping ends the support relationship, rather
        // than leaving them counted twice.
        await f.H.Assignment.AssignAsync(f.TaskId, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = f.HelperId, Reason = "taking it over" });

        var task = await f.H.TaskQueries.GetAsync(f.TaskId);
        Assert.Equal(f.HelperId, task.Value!.PrimaryAssigneeUserId);
        Assert.Empty(task.Value!.SupportPeople);
    }

    [Fact]
    public async Task Adding_a_support_person_tells_everyone_watching_the_task()
    {
        var f = await TaskWithHelperAsync();
        using var _d = f.H;

        // A TaskCollaborator row is not the task row, so the change-tracker interceptor raises
        // nothing for it. Without an explicit announcement, a support person appearing would be
        // invisible to anyone with the task open until they refreshed.
        var published = f.H.Events.Published
            .OfType<WorkflowApp.Application.Common.Events.TaskChangedEvent>()
            .Where(e => e.TaskId == f.TaskId)
            .ToList();

        Assert.NotEmpty(published);
    }

    [Fact]
    public async Task A_supported_task_is_reported_as_help_not_as_owned_work()
    {
        var f = await TaskWithHelperAsync();
        using var _d = f.H;

        var report = await f.H.Reports.DailyUserAsync(
            f.HelperId, f.H.Calendar.ToBusinessDate(f.H.Clock.UtcNow));

        // They own nothing, so nothing may be reported as theirs...
        Assert.Empty(report.OwnedWork);
        Assert.Equal(0, report.TasksWorked);

        // ...but the help itself is still on the record, with the responsible person named so the
        // line cannot be misread as their own work.
        var supporting = Assert.Single(report.SupportingOn);
        Assert.Equal(f.TaskId, supporting.TaskId);
        Assert.Equal("ahsan", supporting.ResponsiblePersonName);
    }
}
