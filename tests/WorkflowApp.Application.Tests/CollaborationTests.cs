using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Application.Tasks.Dtos;
using WorkflowApp.Domain.Enums;
using Xunit;

namespace WorkflowApp.Application.Tests;

/// <summary>
/// Phase 8: comments, dependencies, subtasks, scope changes and reopen. These are the features that
/// let a task carry the context around it — who said what, what it is waiting on, what it grew into.
/// </summary>
public class CollaborationTests
{
    private sealed record Fixture(
        TestHarness H, long TaskId, long RequesterId, long WorkerId, long CoordinatorId, long QCUserId);

    private static async Task<Fixture> TaskAsync()
    {
        var h = await new TestHarness().SeedRolesAndPermissionsAsync();

        var requester = await h.CreateUserAsync("rachel");
        var reviewer = await h.CreateUserAsync("victor");
        var coordinator = await h.CreateUserAsync("amara");
        var worker = await h.CreateUserAsync("wu");
        var qc = await h.CreateUserAsync("quentin");

        h.ActingAsAdmin(reviewer.Id);

        var request = await h.Requests.CreateAsync(requester.Id, new CreateRequestDto
        {
            Title = "Proof-of-delivery photos missing from invoices",
            Description = "POD photos do not appear on the generated PDF.",
            Type = RequestType.Bug
        });

        await h.Triage.DecideAsync(request.Value!.Id, reviewer.Id, new TriageDecisionDto
        {
            Outcome = TriageOutcome.Approve,
            ApprovedPriority = Priority.High,
            EstimatedEffortHours = 6m
        });

        var task = await h.Db.Tasks.SingleAsync();
        h.ActingAsAdmin(coordinator.Id);

        return new Fixture(h, task.Id, requester.Id, worker.Id, coordinator.Id, qc.Id);
    }

    /// <summary>A second, independent task to point dependencies at.</summary>
    private static async Task<long> OtherTaskAsync(Fixture f, string title = "Upgrade the PDF library")
    {
        var created = await f.H.TaskCreation.CreateSubtaskAsync(f.TaskId, f.CoordinatorId,
            new CreateSubtaskDto { Title = title, Description = title });

        // Detach it from the parent so it behaves as a peer, not a child.
        var task = await f.H.Db.Tasks.FirstAsync(t => t.Id == created.Value!.Id);
        task.ParentTaskId = null;
        await f.H.Db.SaveChangesAsync();
        return task.Id;
    }

    // --- comments ----------------------------------------------------------------------------

    [Fact]
    public async Task Comment_visibility_defaults_to_the_category_not_the_caller()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        var internalNote = await f.H.Comments.AddAsync(f.TaskId, f.CoordinatorId,
            new AddCommentDto { Body = "Root cause is the template cache.", Category = CommentCategory.TechnicalNote });

        var toRequester = await f.H.Comments.AddAsync(f.TaskId, f.CoordinatorId,
            new AddCommentDto { Body = "We are on it.", Category = CommentCategory.RequesterCommunication });

        Assert.False(internalNote.Value!.VisibleToRequester);
        Assert.True(toRequester.Value!.VisibleToRequester);
    }

    [Fact]
    public async Task An_explicit_visibility_flag_overrides_the_category_default()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        var shared = await f.H.Comments.AddAsync(f.TaskId, f.CoordinatorId, new AddCommentDto
        {
            Body = "Sharing the technical detail at the customer's request.",
            Category = CommentCategory.TechnicalNote,
            VisibleToRequester = true
        });

        Assert.True(shared.Value!.VisibleToRequester);
    }

    [Fact]
    public async Task The_requester_sees_only_the_comments_written_for_them()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        await f.H.Comments.AddAsync(f.TaskId, f.CoordinatorId,
            new AddCommentDto { Body = "Internal: the estimate was optimistic.", Category = CommentCategory.InternalNote });
        await f.H.Comments.AddAsync(f.TaskId, f.CoordinatorId,
            new AddCommentDto { Body = "We expect a fix on Friday.", Category = CommentCategory.RequesterCommunication });

        // Staff see everything.
        var staffView = await f.H.Comments.ListAsync(f.TaskId, f.CoordinatorId);
        Assert.Equal(2, staffView.Value!.Count);

        // The requester, holding only requester permissions, sees one.
        f.H.ActingAs(f.RequesterId, Permissions.RequestCreate, Permissions.RequestViewOwn);
        var requesterView = await f.H.Comments.ListAsync(f.TaskId, f.RequesterId);

        Assert.Single(requesterView.Value!);
        Assert.Equal("We expect a fix on Friday.", requesterView.Value![0].Body);
    }

    [Fact]
    public async Task A_management_note_is_invisible_and_unwritable_without_the_permission()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        f.H.ActingAs(f.CoordinatorId, Permissions.DashboardManagement);
        await f.H.Comments.AddAsync(f.TaskId, f.CoordinatorId,
            new AddCommentDto { Body = "Escalating to the steering group.", Category = CommentCategory.ManagementNote });

        f.H.ActingAs(f.WorkerId, Permissions.TaskWork);

        var refused = await f.H.Comments.AddAsync(f.TaskId, f.WorkerId,
            new AddCommentDto { Body = "Me too", Category = CommentCategory.ManagementNote });
        Assert.Equal("comment.management_only", refused.Error!.Code);

        var view = await f.H.Comments.ListAsync(f.TaskId, f.WorkerId);
        Assert.Empty(view.Value!);
    }

    // --- dependencies ------------------------------------------------------------------------

    [Fact]
    public async Task A_task_cannot_depend_on_itself()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        var result = await f.H.Dependencies.AddAsync(f.TaskId, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = f.TaskId, Type = DependencyType.DependsOn });

        Assert.Equal("dependency.self_reference", result.Error!.Code);
    }

    [Fact]
    public async Task Parent_child_is_refused_and_points_at_subtasks()
    {
        var f = await TaskAsync();
        using var _d = f.H;
        var other = await OtherTaskAsync(f);

        var result = await f.H.Dependencies.AddAsync(f.TaskId, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = other, Type = DependencyType.ParentChild });

        Assert.Equal("dependency.use_subtasks", result.Error!.Code);
    }

    [Fact]
    public async Task A_circular_ordering_is_refused()
    {
        var f = await TaskAsync();
        using var _d = f.H;
        var other = await OtherTaskAsync(f);

        var first = await f.H.Dependencies.AddAsync(f.TaskId, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = other, Type = DependencyType.DependsOn });
        Assert.True(first.IsSuccess);

        // other now waiting on the task that is already waiting on it.
        var cycle = await f.H.Dependencies.AddAsync(other, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = f.TaskId, Type = DependencyType.DependsOn });

        Assert.Equal("dependency.cycle", cycle.Error!.Code);
    }

    [Fact]
    public async Task A_longer_cycle_is_refused_too()
    {
        var f = await TaskAsync();
        using var _d = f.H;
        var b = await OtherTaskAsync(f, "B");
        var c = await OtherTaskAsync(f, "C");

        await f.H.Dependencies.AddAsync(f.TaskId, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = b, Type = DependencyType.DependsOn });
        await f.H.Dependencies.AddAsync(b, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = c, Type = DependencyType.DependsOn });

        var cycle = await f.H.Dependencies.AddAsync(c, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = f.TaskId, Type = DependencyType.DependsOn });

        Assert.Equal("dependency.cycle", cycle.Error!.Code);
    }

    [Fact]
    public async Task Related_links_do_not_impose_an_order_so_they_never_cycle()
    {
        var f = await TaskAsync();
        using var _d = f.H;
        var other = await OtherTaskAsync(f);

        await f.H.Dependencies.AddAsync(f.TaskId, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = other, Type = DependencyType.Related });

        var back = await f.H.Dependencies.AddAsync(other, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = f.TaskId, Type = DependencyType.Related });

        Assert.True(back.IsSuccess);
        Assert.False(back.Value!.IsBlocked);
    }

    [Fact]
    public async Task An_unfinished_dependency_blocks_the_timer()
    {
        var f = await TaskAsync();
        using var _d = f.H;
        var blocker = await OtherTaskAsync(f);

        await f.H.Dependencies.AddAsync(f.TaskId, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = blocker, Type = DependencyType.DependsOn });

        await f.H.Assignment.AssignAsync(f.TaskId, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = f.WorkerId });
        await f.H.StartShiftAsync(f.WorkerId);
        f.H.ActingAsAdmin(f.WorkerId);

        var start = await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        Assert.Equal("task.blocked_by_dependency", start.Error!.Code);

        // Finishing the blocker clears the way.
        var blockerTask = await f.H.Db.Tasks.FirstAsync(t => t.Id == blocker);
        blockerTask.Status = WorkTaskStatus.Closed;
        await f.H.Db.SaveChangesAsync();

        Assert.True((await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId)).IsSuccess);
    }

    [Fact]
    public async Task Blocks_declared_from_the_other_side_counts_just_the_same()
    {
        var f = await TaskAsync();
        using var _d = f.H;
        var blocker = await OtherTaskAsync(f);

        // Declared on the blocker: "I block the main task."
        await f.H.Dependencies.AddAsync(blocker, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = f.TaskId, Type = DependencyType.Blocks });

        var graph = await f.H.Dependencies.GraphAsync(f.TaskId);

        Assert.True(graph.Value!.IsBlocked);
        Assert.Single(graph.Value.Incoming);
        Assert.True(graph.Value.Incoming[0].IsBlocking);
    }

    [Fact]
    public async Task Removing_a_dependency_unblocks_and_leaves_a_trace()
    {
        var f = await TaskAsync();
        using var _d = f.H;
        var blocker = await OtherTaskAsync(f);

        var added = await f.H.Dependencies.AddAsync(f.TaskId, f.CoordinatorId,
            new AddDependencyDto { RelatedTaskId = blocker, Type = DependencyType.DependsOn });
        Assert.True(added.Value!.IsBlocked);

        var removed = await f.H.Dependencies.RemoveAsync(
            f.TaskId, added.Value.Outgoing[0].Id, f.CoordinatorId);

        Assert.False(removed.Value!.IsBlocked);
        Assert.True(await f.H.Db.TaskActivities.AnyAsync(
            a => a.TaskId == f.TaskId && a.Type == ActivityType.DependencyRemoved));
    }

    // --- subtasks ----------------------------------------------------------------------------

    [Fact]
    public async Task A_subtask_is_a_task_in_its_own_right()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        var created = await f.H.TaskCreation.CreateSubtaskAsync(f.TaskId, f.CoordinatorId,
            new CreateSubtaskDto
            {
                Title = "Backfill last quarter",
                Description = "Regenerate PDFs for Q2.",
                AssigneeUserId = f.WorkerId
            });

        Assert.True(created.IsSuccess);
        var subtask = created.Value!;

        Assert.Equal(f.TaskId, subtask.ParentTaskId);
        Assert.NotEqual("TSK-000001", subtask.TaskNumber);           // its own number
        Assert.Equal(WorkTaskStatus.Assigned, subtask.Status);       // its own assignee
        Assert.Equal(Priority.High, subtask.Priority);               // inherited from the parent

        // Its own history, and a marker on the parent's timeline.
        Assert.True(await f.H.Db.AssignmentHistories.AnyAsync(a => a.TaskId == subtask.Id));
        Assert.True(await f.H.Db.TaskActivities.AnyAsync(
            a => a.TaskId == f.TaskId && a.Type == ActivityType.SubtaskCreated));
    }

    [Fact]
    public async Task Subtasks_do_not_nest()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        var child = await f.H.TaskCreation.CreateSubtaskAsync(f.TaskId, f.CoordinatorId,
            new CreateSubtaskDto { Title = "Child", Description = "Child" });

        var grandchild = await f.H.TaskCreation.CreateSubtaskAsync(child.Value!.Id, f.CoordinatorId,
            new CreateSubtaskDto { Title = "Grandchild", Description = "Grandchild" });

        Assert.Equal("subtask.nesting_not_allowed", grandchild.Error!.Code);
    }

    [Fact]
    public async Task Finished_work_cannot_be_broken_down_further()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        var task = await f.H.Db.Tasks.FirstAsync(t => t.Id == f.TaskId);
        task.Status = WorkTaskStatus.Closed;
        await f.H.Db.SaveChangesAsync();

        var result = await f.H.TaskCreation.CreateSubtaskAsync(f.TaskId, f.CoordinatorId,
            new CreateSubtaskDto { Title = "Late addition", Description = "Too late." });

        Assert.Equal("subtask.parent_finished", result.Error!.Code);
    }

    // --- scope changes -------------------------------------------------------------------------

    [Fact]
    public async Task A_scope_change_does_not_move_the_estimate_until_it_is_approved()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        var change = await f.H.ScopeChanges.RequestAsync(f.TaskId, f.WorkerId, new RequestScopeChangeDto
        {
            Description = "Also needs to cover credit notes.",
            Reason = "Requester confirmed after triage.",
            EstimatedImpactHours = 4m
        });

        Assert.True(change.IsSuccess);
        Assert.Null(change.Value!.ApprovedAt);
        Assert.Equal(6m, (await f.H.Db.Tasks.FirstAsync(t => t.Id == f.TaskId)).EstimatedEffortHours);

        var approved = await f.H.ScopeChanges.ApproveAsync(change.Value.Id, f.CoordinatorId);

        Assert.NotNull(approved.Value!.ApprovedAt);
        Assert.Equal(10m, (await f.H.Db.Tasks.FirstAsync(t => t.Id == f.TaskId)).EstimatedEffortHours);
    }

    [Fact]
    public async Task Approving_a_scope_change_twice_does_not_apply_it_twice()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        var change = await f.H.ScopeChanges.RequestAsync(f.TaskId, f.WorkerId,
            new RequestScopeChangeDto { Description = "More work", EstimatedImpactHours = 4m });

        await f.H.ScopeChanges.ApproveAsync(change.Value!.Id, f.CoordinatorId);
        var again = await f.H.ScopeChanges.ApproveAsync(change.Value.Id, f.CoordinatorId);

        Assert.Equal("scope.already_approved", again.Error!.Code);
        Assert.Equal(10m, (await f.H.Db.Tasks.FirstAsync(t => t.Id == f.TaskId)).EstimatedEffortHours);
    }

    [Fact]
    public async Task A_scope_change_records_who_asked_and_why_so_overruns_stay_readable()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        await f.H.ScopeChanges.RequestAsync(f.TaskId, f.WorkerId, new RequestScopeChangeDto
        {
            Description = "Also needs to cover credit notes.",
            Reason = "Requester confirmed after triage.",
            EstimatedImpactHours = 4m
        });

        var history = await f.H.ScopeChanges.ListAsync(f.TaskId);

        Assert.Single(history);
        Assert.Equal(f.WorkerId, history[0].RequestedByUserId);
        Assert.Equal("Requester confirmed after triage.", history[0].Reason);
        Assert.Equal(4m, history[0].EstimatedImpactHours);
    }

    // --- reopen -------------------------------------------------------------------------------

    /// <summary>Drives the task all the way to Closed so reopening has something to act on.</summary>
    private static async Task<Fixture> ClosedTaskAsync()
    {
        var f = await TaskAsync();

        await f.H.Assignment.AssignAsync(f.TaskId, f.CoordinatorId,
            new AssignTaskDto { AssigneeUserId = f.WorkerId });

        await f.H.StartShiftAsync(f.WorkerId);
        f.H.ActingAsAdmin(f.WorkerId);
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromHours(2));
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Template corrected.");

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto { Result = QCResult.Passed });
        await f.H.Closure.CloseAsync(f.TaskId, f.QCUserId, new CloseTaskDto());

        return f;
    }

    [Fact]
    public async Task Reopening_requires_a_reason_and_only_works_on_closed_work()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        var notClosed = await f.H.Closure.ReopenAsync(f.TaskId, f.CoordinatorId,
            new ReopenTaskDto { Reason = "Customer says it is still broken." });
        Assert.Equal("reopen.not_closed", notClosed.Error!.Code);
    }

    [Fact]
    public async Task Reopening_without_a_reason_is_refused()
    {
        var f = await ClosedTaskAsync();
        using var _d = f.H;

        var result = await f.H.Closure.ReopenAsync(f.TaskId, f.CoordinatorId,
            new ReopenTaskDto { Reason = "   " });

        Assert.Equal("reopen.reason_required", result.Error!.Code);
    }

    [Fact]
    public async Task Reopening_records_the_reason_and_audits_it()
    {
        var f = await ClosedTaskAsync();
        using var _d = f.H;

        var result = await f.H.Closure.ReopenAsync(f.TaskId, f.CoordinatorId,
            new ReopenTaskDto { Reason = "Customer says photos are still missing on credit notes." });

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkTaskStatus.Reopened, result.Value!.Status);

        var history = await f.H.Db.StatusHistories
            .SingleAsync(h => h.TaskId == f.TaskId && h.ToStatus == WorkTaskStatus.Reopened);
        Assert.Contains("credit notes", history.Reason);

        Assert.True(await f.H.Db.AuditLogs.AnyAsync(a => a.Action == "Task.Reopened"));
    }

    [Fact]
    public async Task A_reopened_task_needs_a_fresh_QC_pass_before_it_can_close_again()
    {
        var f = await ClosedTaskAsync();
        using var _d = f.H;

        f.H.Clock.Advance(TimeSpan.FromHours(1));
        await f.H.Closure.ReopenAsync(f.TaskId, f.CoordinatorId,
            new ReopenTaskDto { Reason = "Still broken on credit notes." });

        // The old pass is still on file, but it predates the reopen.
        var check = await f.H.Closure.EvaluateAsync(f.TaskId);
        Assert.False(check.Value!.Requirements.Single(r => r.Code == "closure.qc_passed").IsMet);

        // Rework, re-complete, and QC it again.
        f.H.ActingAsAdmin(f.WorkerId);
        await f.H.WorkSessions.StartAsync(f.TaskId, f.WorkerId);
        f.H.Clock.Advance(TimeSpan.FromMinutes(30));
        await f.H.WorkSessions.CompleteAsync(f.TaskId, f.WorkerId, "Credit notes covered too.");

        f.H.ActingAsAdmin(f.QCUserId);
        await f.H.QC.StartReviewAsync(f.TaskId, f.QCUserId);
        await f.H.QC.SubmitAsync(f.TaskId, f.QCUserId, new SubmitQCReviewDto { Result = QCResult.Passed });

        var reclosed = await f.H.Closure.CloseAsync(f.TaskId, f.QCUserId, new CloseTaskDto());

        Assert.True(reclosed.IsSuccess, reclosed.Error?.Message);
        Assert.Equal(WorkTaskStatus.Closed, reclosed.Value!.Status);
        Assert.Equal(2, await f.H.Db.QCReviews.CountAsync(q => q.TaskId == f.TaskId));
    }

    [Fact]
    public async Task An_open_subtask_keeps_the_parent_from_closing()
    {
        var f = await TaskAsync();
        using var _d = f.H;

        await f.H.TaskCreation.CreateSubtaskAsync(f.TaskId, f.CoordinatorId,
            new CreateSubtaskDto { Title = "Backfill", Description = "Regenerate Q2 PDFs." });

        var check = await f.H.Closure.EvaluateAsync(f.TaskId);

        Assert.False(check.Value!.Requirements.Single(r => r.Code == "closure.subtasks_closed").IsMet);
    }
}
