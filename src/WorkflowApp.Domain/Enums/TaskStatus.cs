namespace WorkflowApp.Domain.Enums;

/// <summary>
/// The enforced task lifecycle. Transitions between these are governed by
/// <see cref="WorkflowApp.Domain.Workflow.TaskWorkflow"/> — not every value can
/// move to every other value.
/// </summary>
public enum WorkTaskStatus
{
    // Intake / triage (task mirrors request early on for a unified pipeline view)
    Requested = 0,
    AwaitingReview = 1,
    ClarificationRequired = 2,
    Approved = 3,

    // Scheduling
    ReadyForAssignment = 10,
    Assigned = 11,
    ReadyToStart = 12,

    // Execution
    InProgress = 20,
    Paused = 21,
    Blocked = 22,

    // QC
    CompletedReadyForQC = 30,
    QCReview = 31,
    QCFailedRework = 32,
    QCPassed = 33,

    // Closure
    ReadyForClosure = 40,
    Closed = 41,

    // Cross-cutting terminal/side states
    Cancelled = 50,
    Deferred = 51,
    OnHold = 52,
    Duplicate = 53,
    Reopened = 54
}
