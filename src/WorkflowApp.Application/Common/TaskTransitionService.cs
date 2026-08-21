using WorkflowApp.Domain.Enums;
using WorkflowApp.Domain.Workflow;

namespace WorkflowApp.Application.Common;

/// <summary>Context passed when attempting a task transition.</summary>
public sealed record TransitionRequest(
    WorkTaskStatus From,
    WorkTaskStatus To,
    IReadOnlySet<string> ActorPermissions,
    string? Reason,
    bool IsOverride = false);

public sealed record TransitionDecision(bool Allowed, string? Error, bool ReasonSatisfied);

/// <summary>
/// Validates a proposed transition against the workflow map + the actor's permissions +
/// reason requirements, BEFORE any persistence happens. The persistence/history/real-time
/// concerns live in the concrete task service (Phase 4) which calls Validate first.
///
/// Kept pure (no DB, no I/O) so it is fully unit-testable.
/// </summary>
public static class TaskTransitionService
{
    public static TransitionDecision Validate(TransitionRequest req)
    {
        // Override bypasses the workflow map but is still recorded and still permission-gated.
        if (req.IsOverride)
        {
            if (!req.ActorPermissions.Contains(Permissions.TaskOverride))
                return new(false, "Override requires Task.Override permission.", false);
            if (string.IsNullOrWhiteSpace(req.Reason))
                return new(false, "Override requires a reason.", false);
            return new(true, null, true);
        }

        var transition = TaskWorkflow.Find(req.From, req.To);
        if (transition is null)
            return new(false, $"Transition {req.From} → {req.To} is not allowed.", false);

        if (!req.ActorPermissions.Contains(transition.RequiredPermission))
            return new(false, $"Missing permission: {transition.RequiredPermission}.", false);

        if (transition.ReasonRequired && string.IsNullOrWhiteSpace(req.Reason))
            return new(false, $"A reason is required to move from {req.From} to {req.To}.", false);

        return new(true, null, true);
    }

    /// <summary>Throwing variant for call sites that prefer exceptions.</summary>
    public static void EnsureValid(TransitionRequest req)
    {
        var d = Validate(req);
        if (!d.Allowed)
        {
            if (d.Error?.Contains("reason is required", StringComparison.OrdinalIgnoreCase) == true)
                throw new TransitionReasonRequiredException(req.From, req.To);
            throw new InvalidWorkflowTransitionException(req.From, req.To);
        }
    }
}
