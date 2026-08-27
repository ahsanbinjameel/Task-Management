namespace WorkflowApp.Domain.Enums;

public enum Priority
{
    Critical = 0,
    High = 1,
    Normal = 2,
    Low = 3
}

/// <summary>Urgency the requester asks for (advisory). Final priority is set at triage.</summary>
public enum RequestedUrgency
{
    Critical = 0,
    High = 1,
    Normal = 2,
    Low = 3
}

public enum RequestStatus
{
    Submitted = 0,
    InReview = 1,
    ClarificationRequired = 2,
    Approved = 3,
    Rejected = 4,
    Duplicate = 5,
    Deferred = 6,
    Escalated = 7,

    /// <summary>
    /// Routed to a checker to find out whether there is really a problem, before anyone decides
    /// whether to build anything. The request has not been approved and no task exists; it comes
    /// back to <see cref="InReview"/> the moment the check reports, whatever it found.
    /// </summary>
    UnderVerification = 8
}

public enum RequestType
{
    Bug = 0,
    ChangeRequest = 1,
    NewFeature = 2,
    Support = 3,
    Configuration = 4,
    Database = 5,
    Report = 6,
    Investigation = 7,
    DataCorrection = 8,
    Infrastructure = 9,
    Other = 99
}

/// <summary>Employee availability during a shift. Distinct from auth and task sessions.</summary>
public enum WorkforceState
{
    NotLoggedIn = 0,
    LoggedInShiftNotStarted = 1,
    Available = 2,
    Working = 3,
    Break = 4,
    Lunch = 5,
    Meeting = 6,
    TemporarilyAway = 7,
    ShiftEnded = 8
}

public enum WorkSessionStatus
{
    Active = 0,
    Paused = 1,
    Completed = 2,
    Interrupted = 3
}

public enum QCResult
{
    Passed = 0,
    Failed = 1,
    ClarificationRequired = 2
}

/// <summary>Categories drive visibility rules (e.g. InternalNote hidden from requester).</summary>
public enum CommentCategory
{
    General = 0,
    RequesterCommunication = 1,
    Clarification = 2,
    InternalNote = 3,
    TechnicalNote = 4,
    ProgressUpdate = 5,
    QCNote = 6,
    ResolutionNote = 7,
    ManagementNote = 8
}

public enum DependencyType
{
    Blocks = 0,
    DependsOn = 1,
    Related = 2,
    Duplicate = 3,
    ParentChild = 4
}

/// <summary>
/// Why work stopped, in the small vocabulary a worker actually recognises.
///
/// The category answers two independent questions, which is the point of having it:
/// whether the <em>task</em> can still move (a client we are waiting on blocks it; lunch does not),
/// and where the <em>person</em> went (lunch changes their availability; waiting for a client does
/// not — they are free to pick up something else). Conflating those two is what made "Paused for
/// lunch" look like a stalled task and left the worker marked Available while they were eating.
/// </summary>
public enum PauseCategory
{
    OtherWorkUrgent = 0,
    WaitingForSomeone = 1,
    WaitingForClient = 2,
    CannotContinue = 3,
    Meeting = 4,
    Break = 5,
    Lunch = 6,
    EndOfShift = 7,
    Other = 8
}

/// <summary>Business timeline event types recorded per task.</summary>
public enum ActivityType
{
    RequestSubmitted, RequestEdited, ReviewStarted, ClarificationRequested,
    ClarificationAnswered, RequestApproved, RequestRejected, TaskCreated,
    PriorityChanged, AssignmentChanged, CollaboratorAdded, TaskStarted,
    TaskPaused, TaskResumed, TaskBlocked, TaskUnblocked, ScopeChanged,
    TaskCompleted, QCStarted, QCFailed, QCPassed, TaskClosed, TaskReopened,
    TaskInterrupted,

    // Phase 8. Appended, never reordered - the values are persisted as ints.
    CommentAdded, DependencyAdded, DependencyRemoved, SubtaskCreated, ScopeChangeApproved,

    // Stabilisation pass. Appended for the same reason.
    CollaboratorRemoved,

    // Verification. Appended for the same reason - these are persisted as ints.
    VerificationRequested, VerificationAssigned, VerificationStarted,
    VerificationCompleted, VerificationCancelled
}

/// <summary>
/// Where an assigned check has got to.
///
/// Deliberately short. A verification is an investigation, not a project: it is asked for, given to
/// somebody, looked at, and reported on. Anything more would be a second task lifecycle, and the
/// system already has one of those.
/// </summary>
public enum VerificationStatus
{
    /// <summary>Raised, nobody looking at it yet.</summary>
    Requested = 0,

    /// <summary>A checker has it; they have not started.</summary>
    Assigned = 1,

    /// <summary>Being looked at now.</summary>
    InProgress = 2,

    /// <summary>Reported on. Carries a <see cref="VerificationResult"/> and findings.</summary>
    Completed = 3,

    /// <summary>Called off. Kept, with its reason, rather than deleted.</summary>
    Cancelled = 4
}

/// <summary>
/// What the checker found.
///
/// These belong to the verification, not to any task workflow: the question being answered is
/// "is there actually a problem?", and most of the useful answers are not "yes, build something".
/// <see cref="IssueConfirmed"/> in particular creates nothing — it hands the request back to a
/// reviewer, who still has to approve it explicitly before any work exists.
/// </summary>
public enum VerificationResult
{
    /// <summary>There is a real problem. Still not a task: the reviewer decides that.</summary>
    IssueConfirmed = 0,

    /// <summary>Behaving as designed. Nothing to build.</summary>
    WorkingCorrectly = 1,

    /// <summary>Real, but it is settings or data rather than software.</summary>
    ConfigurationOrDataIssue = 2,

    /// <summary>Cannot answer without more from whoever raised it.</summary>
    NeedsClarification = 3,

    /// <summary>Looked, could not reproduce or could not tell. An honest answer, and a common one.</summary>
    Inconclusive = 4
}

/// <summary>
/// What kind of thing is being checked.
///
/// Only two of these are aggregates this database holds, and those two get real foreign keys
/// (<c>RequestId</c>, <c>ModuleId</c>). The rest are things the system knows about but does not
/// model — a screen in another application, a deployed build — and they are described in
/// <c>TargetName</c>/<c>TargetReference</c> rather than pointed at by an untyped id column.
/// </summary>
public enum VerificationTargetType
{
    /// <summary>The request itself, as submitted. The triage route.</summary>
    Request = 0,

    /// <summary>A screen or form. Not an aggregate here — named, not referenced.</summary>
    Form = 1,

    /// <summary>A module of a project. A real row: see <c>Verification.ModuleId</c>.</summary>
    Module = 2,

    /// <summary>A deployed build. Named by version in <c>TargetReference</c>.</summary>
    Build = 3,

    /// <summary>Anything else somebody needs looked at.</summary>
    Other = 99
}
