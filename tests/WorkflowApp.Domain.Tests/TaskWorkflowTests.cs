using WorkflowApp.Application.Common;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Workflow;
using Xunit;

namespace WorkflowApp.Domain.Tests;

public class TaskWorkflowTests
{
    [Fact]
    public void Allows_valid_happy_path_transition()
    {
        Assert.True(TaskWorkflow.IsAllowed(WorkTaskStatus.ReadyToStart, WorkTaskStatus.InProgress));
    }

    [Fact]
    public void Rejects_illegal_shortcut_assigned_to_closed()
    {
        // The core requirement: cannot skip from Assigned straight to Closed.
        Assert.False(TaskWorkflow.IsAllowed(WorkTaskStatus.Assigned, WorkTaskStatus.Closed));
    }

    [Fact]
    public void Failed_qc_returns_to_rework_then_in_progress()
    {
        Assert.True(TaskWorkflow.IsAllowed(WorkTaskStatus.QCReview, WorkTaskStatus.QCFailedRework));
        Assert.True(TaskWorkflow.IsAllowed(WorkTaskStatus.QCFailedRework, WorkTaskStatus.InProgress));
    }

    [Fact]
    public void Pause_requires_reason()
    {
        var perms = new HashSet<string> { TaskWorkflow.PermWork };
        var decision = TaskTransitionService.Validate(
            new TransitionRequest(WorkTaskStatus.InProgress, WorkTaskStatus.Paused, perms, Reason: null));
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void Pause_with_reason_and_permission_is_allowed()
    {
        var perms = new HashSet<string> { TaskWorkflow.PermWork };
        var decision = TaskTransitionService.Validate(
            new TransitionRequest(WorkTaskStatus.InProgress, WorkTaskStatus.Paused, perms, Reason: "Lunch"));
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Transition_blocked_without_permission()
    {
        var perms = new HashSet<string>(); // no permissions
        var decision = TaskTransitionService.Validate(
            new TransitionRequest(WorkTaskStatus.ReadyToStart, WorkTaskStatus.InProgress, perms, Reason: null));
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void Override_needs_override_permission_and_reason()
    {
        var noPerm = TaskTransitionService.Validate(new TransitionRequest(
            WorkTaskStatus.Assigned, WorkTaskStatus.Closed, new HashSet<string>(), "x", IsOverride: true));
        Assert.False(noPerm.Allowed);

        var ok = TaskTransitionService.Validate(new TransitionRequest(
            WorkTaskStatus.Assigned, WorkTaskStatus.Closed,
            new HashSet<string> { Permissions.TaskOverride }, "Emergency close", IsOverride: true));
        Assert.True(ok.Allowed);
    }

    [Fact]
    public void Cancel_allowed_from_active_state_but_not_from_closed()
    {
        Assert.True(TaskWorkflow.IsAllowed(WorkTaskStatus.InProgress, WorkTaskStatus.Cancelled));
        Assert.False(TaskWorkflow.IsAllowed(WorkTaskStatus.Closed, WorkTaskStatus.Cancelled));
    }
}
