using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Domain.Workflow;

/// <summary>
/// One allowed workforce-state transition. <see cref="Label"/> is the human-readable text written
/// to the activity timeline, which is why it lives on the transition rather than at the call site:
/// "Lunch Started" should read identically whether the user came from Available or from Working.
/// </summary>
public sealed record WorkforceTransition(
    WorkforceState From,
    WorkforceState To,
    string Label);

/// <summary>
/// The employee availability state machine — the workforce counterpart to
/// <see cref="TaskWorkflow"/>. Dependency-free and exhaustive: a transition that is not declared
/// here cannot happen, so availability can never drift into a nonsensical state.
///
/// This governs shape only. Cross-aggregate rules (you cannot end a shift while a work session is
/// still open) belong to the application service, because they need to look outside this aggregate.
/// </summary>
public static class WorkforceStateMachine
{
    public static readonly IReadOnlyList<WorkforceTransition> Transitions = new List<WorkforceTransition>
    {
        // --- Authentication boundary ---
        new(WorkforceState.NotLoggedIn,              WorkforceState.LoggedInShiftNotStarted, "Logged In"),
        new(WorkforceState.LoggedInShiftNotStarted,  WorkforceState.NotLoggedIn,             "Logged Out"),

        // --- Opening a shift ---
        new(WorkforceState.LoggedInShiftNotStarted,  WorkforceState.Available,               "Shift Started"),
        // A second shift on the same day (split shifts) starts straight from ShiftEnded.
        new(WorkforceState.ShiftEnded,               WorkforceState.Available,               "Shift Started"),

        // --- Available ↔ Working ---
        // Working is entered by starting a task (Phase 6), never by asking for it directly.
        new(WorkforceState.Available,                WorkforceState.Working,                 "Started Working"),
        new(WorkforceState.Working,                  WorkforceState.Available,               "Stopped Working"),

        // --- Stepping away (from Available) ---
        new(WorkforceState.Available,                WorkforceState.Break,                   "Break Started"),
        new(WorkforceState.Available,                WorkforceState.Lunch,                   "Lunch Started"),
        new(WorkforceState.Available,                WorkforceState.Meeting,                 "Meeting Started"),
        new(WorkforceState.Available,                WorkforceState.TemporarilyAway,         "Stepped Away"),

        // --- Stepping away (from Working) ---
        // The task's work session is paused by the same operation; see ShiftService.
        new(WorkforceState.Working,                  WorkforceState.Break,                   "Break Started"),
        new(WorkforceState.Working,                  WorkforceState.Lunch,                   "Lunch Started"),
        new(WorkforceState.Working,                  WorkforceState.Meeting,                 "Meeting Started"),
        new(WorkforceState.Working,                  WorkforceState.TemporarilyAway,         "Stepped Away"),

        // --- Coming back ---
        new(WorkforceState.Break,                    WorkforceState.Available,               "Break Ended"),
        new(WorkforceState.Lunch,                    WorkforceState.Available,               "Lunch Ended"),
        new(WorkforceState.Meeting,                  WorkforceState.Available,               "Meeting Ended"),
        new(WorkforceState.TemporarilyAway,          WorkforceState.Available,               "Returned"),

        // Resuming a task directly, without passing through Available.
        new(WorkforceState.Break,                    WorkforceState.Working,                 "Resumed Working"),
        new(WorkforceState.Lunch,                    WorkforceState.Working,                 "Resumed Working"),
        new(WorkforceState.Meeting,                  WorkforceState.Working,                 "Resumed Working"),
        new(WorkforceState.TemporarilyAway,          WorkforceState.Working,                 "Resumed Working"),

        // --- Closing a shift ---
        // Permitted from every on-shift state. Ending a shift while a task is running is stopped
        // by the service, not here, because that rule depends on the work-session aggregate.
        new(WorkforceState.Available,                WorkforceState.ShiftEnded,              "Shift Ended"),
        new(WorkforceState.Working,                  WorkforceState.ShiftEnded,              "Shift Ended"),
        new(WorkforceState.Break,                    WorkforceState.ShiftEnded,              "Shift Ended"),
        new(WorkforceState.Lunch,                    WorkforceState.ShiftEnded,              "Shift Ended"),
        new(WorkforceState.Meeting,                  WorkforceState.ShiftEnded,              "Shift Ended"),
        new(WorkforceState.TemporarilyAway,          WorkforceState.ShiftEnded,              "Shift Ended"),

        // --- Logging out ---
        new(WorkforceState.ShiftEnded,               WorkforceState.NotLoggedIn,             "Logged Out"),
    }.AsReadOnly();

    /// <summary>States in which the employee is on shift and their time is being accounted for.</summary>
    private static readonly HashSet<WorkforceState> OnShiftStates = new()
    {
        WorkforceState.Available, WorkforceState.Working, WorkforceState.Break,
        WorkforceState.Lunch, WorkforceState.Meeting, WorkforceState.TemporarilyAway
    };

    /// <summary>On shift but not at the desk. Time here is shift time, not productive time.</summary>
    private static readonly HashSet<WorkforceState> AwayStates = new()
    {
        WorkforceState.Break, WorkforceState.Lunch,
        WorkforceState.Meeting, WorkforceState.TemporarilyAway
    };

    /// <summary>
    /// States a user may move to on their own, via the change-state endpoint. Deliberately excludes
    /// <see cref="WorkforceState.Working"/> (entered by starting a task) and
    /// <see cref="WorkforceState.ShiftEnded"/> (entered by ending the shift), so availability can
    /// never claim work that is not actually happening.
    /// </summary>
    private static readonly HashSet<WorkforceState> SelfServiceStates = new()
    {
        WorkforceState.Available, WorkforceState.Break,
        WorkforceState.Lunch, WorkforceState.Meeting, WorkforceState.TemporarilyAway
    };

    public static WorkforceTransition? Find(WorkforceState from, WorkforceState to) =>
        Transitions.FirstOrDefault(t => t.From == from && t.To == to);

    public static bool IsAllowed(WorkforceState from, WorkforceState to) => Find(from, to) is not null;

    public static IEnumerable<WorkforceState> NextStates(WorkforceState from) =>
        Transitions.Where(t => t.From == from).Select(t => t.To).Distinct();

    /// <summary>The states a user may pick themselves from where they currently are.</summary>
    public static IEnumerable<WorkforceState> SelfServiceNextStates(WorkforceState from) =>
        NextStates(from).Where(IsSelfServiceTarget);

    public static bool IsOnShift(WorkforceState state) => OnShiftStates.Contains(state);

    public static bool IsAway(WorkforceState state) => AwayStates.Contains(state);

    /// <summary>Only <see cref="WorkforceState.Working"/> counts toward productive time.</summary>
    public static bool IsProductive(WorkforceState state) => state == WorkforceState.Working;

    public static bool IsSelfServiceTarget(WorkforceState state) => SelfServiceStates.Contains(state);
}

/// <summary>Thrown when a caller attempts a workforce transition the state machine does not permit.</summary>
public sealed class InvalidWorkforceTransitionException : Exception
{
    public WorkforceState From { get; }
    public WorkforceState To { get; }

    public InvalidWorkforceTransitionException(WorkforceState from, WorkforceState to)
        : base($"Workforce transition from {from} to {to} is not allowed.")
    {
        From = from;
        To = to;
    }
}
