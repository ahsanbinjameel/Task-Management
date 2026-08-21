using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Domain.Workflow;

/// <summary>Thrown when a caller attempts a transition the workflow does not permit.</summary>
public sealed class InvalidWorkflowTransitionException : Exception
{
    public WorkTaskStatus From { get; }
    public WorkTaskStatus To { get; }

    public InvalidWorkflowTransitionException(WorkTaskStatus from, WorkTaskStatus to)
        : base($"Transition from {from} to {to} is not allowed.")
    {
        From = from;
        To = to;
    }
}

/// <summary>Thrown when a transition requires a reason but none was supplied.</summary>
public sealed class TransitionReasonRequiredException : Exception
{
    public TransitionReasonRequiredException(WorkTaskStatus from, WorkTaskStatus to)
        : base($"A reason is required to move from {from} to {to}.") { }
}
