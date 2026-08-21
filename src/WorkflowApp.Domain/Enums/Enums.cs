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
    Escalated = 7
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

/// <summary>Business timeline event types recorded per task.</summary>
public enum ActivityType
{
    RequestSubmitted, RequestEdited, ReviewStarted, ClarificationRequested,
    ClarificationAnswered, RequestApproved, RequestRejected, TaskCreated,
    PriorityChanged, AssignmentChanged, CollaboratorAdded, TaskStarted,
    TaskPaused, TaskResumed, TaskBlocked, TaskUnblocked, ScopeChanged,
    TaskCompleted, QCStarted, QCFailed, QCPassed, TaskClosed, TaskReopened,
    TaskInterrupted
}
