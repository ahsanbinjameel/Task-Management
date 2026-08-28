using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Domain.Workflow;

/// <summary>
/// Declarative definition of a single allowed task-status transition.
/// The presence of a transition in <see cref="TaskWorkflow.Transitions"/> is what makes it legal.
/// Anything not declared here is rejected by the workflow engine.
/// </summary>
public sealed record WorkflowTransition(
    WorkTaskStatus From,
    WorkTaskStatus To,
    string RequiredPermission,
    bool ReasonRequired = false);

/// <summary>
/// The single source of truth for the task lifecycle. This is intentionally a plain,
/// dependency-free class so it is trivially unit-testable and readable by anyone auditing
/// the business rules. The Application layer wraps this with persistence, history writes,
/// and real-time events.
/// </summary>
public static class TaskWorkflow
{
    // Permission constants (mirrored in the Application permission catalog in Phase 1).
    public const string PermReview      = "Task.Review";
    public const string PermApprove     = "Task.Approve";
    public const string PermAssign      = "Task.Assign";
    public const string PermWork        = "Task.Work";
    public const string PermQC          = "Task.QCReview";
    public const string PermClose       = "Task.Close";
    public const string PermReopen      = "Task.Reopen";
    public const string PermCancel      = "Task.Cancel";
    public const string PermDefer       = "Task.Defer";

    public static readonly IReadOnlyList<WorkflowTransition> Transitions = new List<WorkflowTransition>
    {
        // --- Intake / triage ---
        new(WorkTaskStatus.Requested,             WorkTaskStatus.AwaitingReview,        PermReview),
        new(WorkTaskStatus.AwaitingReview,        WorkTaskStatus.ClarificationRequired, PermReview, ReasonRequired: true),
        new(WorkTaskStatus.ClarificationRequired, WorkTaskStatus.AwaitingReview,        PermReview),
        new(WorkTaskStatus.AwaitingReview,        WorkTaskStatus.Approved,              PermApprove),
        new(WorkTaskStatus.AwaitingReview,        WorkTaskStatus.Duplicate,             PermReview, ReasonRequired: true),

        // --- Scheduling ---
        new(WorkTaskStatus.Approved,              WorkTaskStatus.ReadyForAssignment,    PermApprove),
        new(WorkTaskStatus.ReadyForAssignment,    WorkTaskStatus.Assigned,              PermAssign),
        new(WorkTaskStatus.Assigned,              WorkTaskStatus.ReadyToStart,          PermAssign),
        new(WorkTaskStatus.Assigned,              WorkTaskStatus.ReadyForAssignment,    PermAssign, ReasonRequired: true), // reassign

        // --- Execution ---
        new(WorkTaskStatus.ReadyToStart,          WorkTaskStatus.InProgress,            PermWork),
        new(WorkTaskStatus.InProgress,            WorkTaskStatus.Paused,                PermWork, ReasonRequired: true),
        new(WorkTaskStatus.Paused,                WorkTaskStatus.InProgress,            PermWork),
        new(WorkTaskStatus.InProgress,            WorkTaskStatus.Blocked,               PermWork, ReasonRequired: true),
        new(WorkTaskStatus.Blocked,               WorkTaskStatus.InProgress,            PermWork),
        new(WorkTaskStatus.InProgress,            WorkTaskStatus.CompletedReadyForQC,   PermWork),

        // --- QC ---
        new(WorkTaskStatus.CompletedReadyForQC,   WorkTaskStatus.QCReview,              PermQC),
        new(WorkTaskStatus.QCReview,              WorkTaskStatus.QCFailedRework,        PermQC, ReasonRequired: true),
        new(WorkTaskStatus.QCFailedRework,        WorkTaskStatus.InProgress,            PermWork),
        new(WorkTaskStatus.QCReview,              WorkTaskStatus.QCPassed,              PermQC),

        // --- Closure ---
        new(WorkTaskStatus.QCPassed,              WorkTaskStatus.ReadyForClosure,       PermClose),
        new(WorkTaskStatus.ReadyForClosure,       WorkTaskStatus.Closed,                PermClose),

        // --- Reopen ---
        new(WorkTaskStatus.Closed,                WorkTaskStatus.Reopened,              PermReopen, ReasonRequired: true),
        new(WorkTaskStatus.Reopened,              WorkTaskStatus.InProgress,            PermWork),

        // The requester's "still not fixed" (PRODUCT-CORE §7). Internal quality check and client
        // acceptance are different claims, so work that has passed ours can still come back from
        // the person who asked — before it was ever closed. It lands in Reopened rather than
        // QCFailedRework because that verdict belongs to QC and is reachable only through QCService,
        // and because Reopened already resets the closure gate's QC requirement: a rejected fix has
        // to be checked again before it can close.
        //
        // These say only that the move is *shaped* correctly. Who may make it is decided on the
        // record by ClosureService — the requester of the originating request, not a permission
        // holder — the same split the rest of the app uses.
        new(WorkTaskStatus.QCPassed,              WorkTaskStatus.Reopened,              PermReopen, ReasonRequired: true),
        new(WorkTaskStatus.ReadyForClosure,       WorkTaskStatus.Reopened,              PermReopen, ReasonRequired: true),

        // --- Cross-cutting side states (allowed from most active states) ---
        new(WorkTaskStatus.Approved,              WorkTaskStatus.Deferred,              PermDefer, ReasonRequired: true),
        new(WorkTaskStatus.ReadyForAssignment,    WorkTaskStatus.Deferred,              PermDefer, ReasonRequired: true),
        new(WorkTaskStatus.Deferred,              WorkTaskStatus.ReadyForAssignment,    PermAssign),
        new(WorkTaskStatus.Assigned,              WorkTaskStatus.OnHold,                PermAssign, ReasonRequired: true),
        new(WorkTaskStatus.OnHold,                WorkTaskStatus.Assigned,              PermAssign),
    }.AsReadOnly();

    /// <summary>Cancellation is permitted from any non-terminal state.</summary>
    private static readonly HashSet<WorkTaskStatus> TerminalStates = new()
    {
        WorkTaskStatus.Closed, WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate
    };

    /// <summary>Find the transition rule for a From→To pair, or null if it isn't allowed.</summary>
    public static WorkflowTransition? Find(WorkTaskStatus from, WorkTaskStatus to)
    {
        if (to == WorkTaskStatus.Cancelled && !TerminalStates.Contains(from))
            return new WorkflowTransition(from, WorkTaskStatus.Cancelled, PermCancel, ReasonRequired: true);

        return Transitions.FirstOrDefault(t => t.From == from && t.To == to);
    }

    public static bool IsAllowed(WorkTaskStatus from, WorkTaskStatus to) => Find(from, to) is not null;

    public static IEnumerable<WorkTaskStatus> NextStates(WorkTaskStatus from) =>
        Transitions.Where(t => t.From == from).Select(t => t.To);
}
