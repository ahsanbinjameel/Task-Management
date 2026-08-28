using System.ComponentModel.DataAnnotations;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Dtos;

public sealed record TransitionTaskDto
{
    [Required]
    public WorkTaskStatus To { get; init; }

    [MaxLength(1000)]
    public string? Reason { get; init; }

    /// <summary>
    /// Forces a transition the workflow map does not allow. Requires <c>Task.Override</c> and a
    /// reason, and is always recorded as an override in the history.
    /// </summary>
    public bool IsOverride { get; init; }

    /// <summary>
    /// Client-supplied key that makes a retry safe. Sending the same key twice performs the
    /// transition once — this is what stops a double-clicked button producing two history rows.
    /// </summary>
    [MaxLength(100)]
    public string? IdempotencyKey { get; init; }
}

public sealed record AssignTaskDto
{
    public long? AssigneeUserId { get; init; }

    [MaxLength(1000)]
    public string? Reason { get; init; }

    /// <summary>
    /// The task's concurrency token as the client last saw it. Two coordinators assigning the same
    /// task at once: the second one is rejected rather than silently overwriting the first.
    /// </summary>
    public string? RowVersion { get; init; }
}

public sealed record AddCollaboratorDto
{
    [Required]
    public long UserId { get; init; }
}

public sealed record SetTaskRolesDto
{
    public long? ReviewerUserId { get; init; }
    public long? QCUserId { get; init; }
}

public sealed record ReorderQueueDto
{
    /// <summary>Task ids in the order the assignee wants to work them, first to last.</summary>
    [Required]
    public IReadOnlyList<long> TaskIdsInOrder { get; init; } = Array.Empty<long>();
}

public sealed record UpdateTaskDetailsDto
{
    public Priority? Priority { get; init; }
    public decimal? EstimatedEffortHours { get; init; }
    public DateTimeOffset? DueDate { get; init; }

    [MaxLength(4000)] public string? AcceptanceCriteria { get; init; }
    [MaxLength(4000)] public string? Resolution { get; init; }

    [Range(0, 100)] public int? ProgressPercent { get; init; }
}

public sealed record TaskSummaryDto(
    long Id,
    string TaskNumber,
    string Title,
    RequestType Type,
    WorkTaskStatus Status,
    Priority Priority,
    long? PrimaryAssigneeUserId,
    string? PrimaryAssigneeDisplayName,
    DateTimeOffset? DueDate,
    int QueueOrder,
    int ProgressPercent,
    decimal? EstimatedEffortHours,
    TimeSpan TotalWorkedTime,
    bool HasActiveSession,
    // Queues and lists are where "whose work is this?" gets asked most often.
    long? ClientId = null,
    string? ClientName = null,

    // ---- what the contextual grids need ------------------------------------------------------
    //
    // Each view shows the two or three dates that matter to it: how long something has waited to
    // be picked up, when work actually started, who checked it and when it came back. All of it is
    // already recorded in the history tables — the row just has to carry it, so nobody opens a
    // task to find out how stale it is.
    /// <summary>When it entered the status it is in — the "waiting since" every queue asks for.</summary>
    DateTimeOffset? StatusSince = null,
    /// <summary>The reason recorded for that move, where one was required (paused, blocked, held).</summary>
    string? StatusReason = null,
    DateTimeOffset? AssignedAt = null,
    /// <summary>First time anyone actually started the timer on it.</summary>
    DateTimeOffset? StartedAt = null,
    /// <summary>When it was handed to quality check.</summary>
    DateTimeOffset? CompletedAt = null,
    long? RequestId = null,
    string? RequestNumber = null,
    string? RequestedByDisplayName = null,
    /// <summary>Latest quality check: who looked at it, when, and what they said.</summary>
    string? CheckedByDisplayName = null,
    DateTimeOffset? CheckedAt = null,
    string? CheckNotes = null,
    /// <summary>Who is lined up to check it, where that has been decided.</summary>
    string? QCUserDisplayName = null,
    IReadOnlyList<string>? SupportPeople = null,

    // ---- what a worker needs before they can start (PRODUCT-CORE §12A) ------------------------
    //
    // A queue row has to answer "what is this, in what part of the product, what is it supposed to
    // do, and is there a picture" without opening anything. Those four questions were the ones
    // being asked of Ahsan by message, which is exactly the relay the software exists to remove.
    /// <summary>The product area, beside the client. Together these are the ERP context.</summary>
    long? ModuleId = null,
    string? ModuleName = null,
    /// <summary>
    /// Module, form and surface joined for reading — "Sales · Delivery Order · Detail Report".
    /// Null when triage has not placed it yet, so a row can leave the line out entirely.
    /// </summary>
    string? ProductLocation = null,
    /// <summary>What "working" is supposed to look like, in the requester's own words.</summary>
    string? ExpectedResult = null,
    /// <summary>
    /// Everything worth looking at before starting: the requester's screenshots and anything filed
    /// against the task itself. Quality-check evidence is deliberately excluded — it belongs to a
    /// numbered attempt, not to the task.
    /// </summary>
    int AttachmentCount = 0);

/// <summary>
/// A status transition, from-and-to. This is the *technical* trail: it is the shape of the state
/// machine written down, and it says `CompletedReadyForQC -> QCReview` because that is what
/// happened. Read by people who run the process. The readable account is
/// <see cref="TaskActivityDto"/>.
/// </summary>
public sealed record StatusHistoryDto(
    long Id,
    WorkTaskStatus FromStatus,
    WorkTaskStatus ToStatus,
    long ChangedByUserId,
    /// <summary>Named, not numbered — a trail of user ids is a trail nobody can read.</summary>
    string? ChangedByDisplayName,
    DateTimeOffset ChangedAt,
    string? Reason,
    bool WasOverride);

public sealed record AssignmentHistoryDto(
    long Id,
    long? FromUserId,
    string? FromDisplayName,
    long? ToUserId,
    string? ToDisplayName,
    long AssignedByUserId,
    string? AssignedByDisplayName,
    DateTimeOffset AssignedAt,
    string? Reason);

/// <summary>
/// What happened, in a sentence, in the order it happened. The account a person reads.
///
/// Deliberately distinct from both <see cref="StatusHistoryDto"/> (the state machine's own record)
/// and the audit log (the administrator's before-and-after). One story per audience: merging them
/// produced a timeline where "Wu started work" sat between two rows of enum names, and the reader
/// had to filter it themselves every time.
/// </summary>
public sealed record TaskActivityDto(
    long Id,
    ActivityType Type,
    long ActorUserId,
    string? ActorDisplayName,
    DateTimeOffset OccurredAt,
    string Description);

public sealed record WorkSessionDto(
    long Id,
    long TaskId,
    long UserId,
    DateTimeOffset SessionStart,
    DateTimeOffset? SessionEnd,
    TimeSpan? Duration,
    WorkSessionStatus Status,
    long? EndPauseReasonId,
    string? EndPauseReasonName,
    string? EndComment,
    bool EndedByInterruption,
    long? InterruptedByTaskId);

/// <summary>
/// What was originally asked for, carried onto the task that came out of it.
///
/// The two records stay separate — a request is not a task, and merging them would wreck the one
/// rule the whole workflow rests on. What is wrong is making a worker go and *read* the request to
/// find the screenshot or the expected result. So the request's own words travel with the work.
/// </summary>
public sealed record RequestContextDto(
    long RequestId,
    string RequestNumber,
    string RequestedByDisplayName,
    DateTimeOffset RequestedAt,
    RequestedUrgency RequestedUrgency,
    string? ProjectName,
    string? ModuleName,
    /// <summary>The description as the requester wrote it, before triage reworded anything.</summary>
    string OriginalDescription,
    string? BusinessImpact,
    string? ExpectedResult,
    string? CurrentResult,
    string? ReproductionSteps,
    /// <summary>Files attached to the request — usually the screenshots.</summary>
    IReadOnlyList<AttachmentDto> Attachments,
    /// <summary>The submission this arrived in, when it arrived alongside others.</summary>
    long? BatchId = null,
    string? BatchNumber = null,
    /// <summary>
    /// The other requests folded into this same task, if a reviewer combined several. A worker
    /// handed three folded items has to be able to see that three separate things were asked for —
    /// otherwise "done" gets declared when only the first one is.
    /// </summary>
    IReadOnlyList<FoldedRequestDto>? FoldedWith = null);

/// <summary>One of the other requests a reviewer folded into the same task.</summary>
public sealed record FoldedRequestDto(
    long RequestId,
    string RequestNumber,
    string Title,
    string Description,
    string RequestedByDisplayName);

public sealed record TaskDetailDto(
    long Id,
    string TaskNumber,
    long? RequestId,
    string? RequestNumber,
    string Title,
    string Description,
    RequestType Type,
    WorkTaskStatus Status,
    Priority Priority,
    long? ClientId,
    // The name as well as the id: a screen cannot show "ABC Company" from a number, which is why
    // the client was invisible everywhere despite being on the record.
    string? ClientName,
    /// <summary>
    /// The other axis (PRODUCT-CORE §5): "Sales · Delivery Order · Detail Report". The client says
    /// which instance, this says which part of the product. Null until triage places it.
    /// </summary>
    string? ProductLocation,
    long? PrimaryAssigneeUserId,
    string? PrimaryAssigneeDisplayName,
    long? ReviewerUserId,
    long? QCUserId,
    decimal? EstimatedEffortHours,
    DateTimeOffset? DueDate,
    string? AcceptanceCriteria,
    string? Resolution,
    int ProgressPercent,
    int QueueOrder,
    long? ParentTaskId,
    // What this task may legally move to next, given the caller's permissions.
    IReadOnlyList<WorkTaskStatus> AvailableTransitions,
    TimeSpan TotalWorkedTime,
    // Support people: they helped, they do not own this. Never counted as assignment.
    IReadOnlyList<SupportPersonDto> SupportPeople,
    IReadOnlyList<WorkSessionDto> WorkSessions,
    IReadOnlyList<StatusHistoryDto> StatusHistory,
    IReadOnlyList<AssignmentHistoryDto> AssignmentHistory,
    IReadOnlyList<TaskActivityDto> Activity,
    IReadOnlyList<QCReviewDto> QCReviews,
    // The smaller tasks this one was broken into, shown on the parent's own page.
    IReadOnlyList<SubtaskSummaryDto> SubTasks,
    // Task numbers of unfinished work this task is waiting on. Non-empty blocks the timer.
    IReadOnlyList<string> BlockedBy,
    string? RowVersion,
    /// <summary>Where this work came from. Null for a task with no request behind it.</summary>
    RequestContextDto? Request = null,
    /// <summary>
    /// What the responsible person attached as proof the work is done.
    ///
    /// Kept apart from the request's own screenshots, which describe the problem rather than the
    /// fix. Merged into one list they would be indistinguishable, and "show me the evidence this
    /// was actually done" — the question a closure decision turns on — could not be asked.
    ///
    /// Append-only, like everything else here: work that failed a check and was completed again
    /// keeps both sets, oldest first, so a reader can see what changed between the attempts.
    /// </summary>
    IReadOnlyList<Requests.Dtos.AttachmentDto>? CompletionProof = null,
    /// <summary>Files added to the task for context, rather than as proof of anything.</summary>
    IReadOnlyList<Requests.Dtos.AttachmentDto>? Attachments = null);

/// <summary>
/// Someone helping with a task who does not own it.
///
/// Deliberately a separate shape from the assignee: a support person never appears in a queue, a
/// task count, an overdue figure or a workload total. They are shown so the people involved are
/// visible, and so reports can credit the help separately from the responsibility.
/// </summary>
/// <summary>
/// A smaller task belonging to a parent, summarised for display on the parent's page so the whole
/// structure can be understood without navigating away.
/// </summary>
public sealed record SubtaskSummaryDto(
    long TaskId,
    string TaskNumber,
    string Title,
    WorkTaskStatus Status,
    string? ResponsiblePersonName,
    int ProgressPercent,
    /// <summary>When true the parent cannot be finished until this one is done.</summary>
    bool IsRequired);

public sealed record SupportPersonDto(
    long UserId,
    string DisplayName,
    DateTimeOffset AddedAt,
    long AddedByUserId);

/// <summary>
/// A reason work stopped. Carries both axes so the client can group the list and explain the
/// consequence, rather than guessing from the name.
/// </summary>
public sealed record PauseReasonDto(
    long Id,
    string Name,
    bool RequiresComment,
    /// <summary>The task itself cannot move on — not merely that the worker stepped away.</summary>
    bool IsBlocker,
    PauseCategory Category,
    /// <summary>Where the worker goes, if anywhere. Null means they stay on shift and free.</summary>
    WorkforceState? AwayState);

/// <summary>Minimal directory entry for the assign dialog — id and name, nothing more.</summary>
public sealed record AssignableUserDto(long Id, string UserName, string DisplayName, WorkforceState WorkforceState);

/// <summary>
/// One person a specific task could be given to, described in facts (PRODUCT-CORE §12C).
///
/// Deliberately <b>no capacity number</b>. The old panel summed estimated hours and called it
/// capacity, which is a figure nobody could act on: estimates are guesses, most tasks carry none at
/// all, and adding guesses together does not produce a fact. What a coordinator actually asks is
/// "is this person here, what are they on right now, how much is already queued behind it, and have
/// they touched this part of the product before" — so that is what this carries, and the person
/// decides.
///
/// Everyone who may hold work appears, including people with nothing on. A panel built only from
/// people who already have tasks cannot answer "who is free", which is most of the question.
/// </summary>
public sealed record AssignmentCandidateDto(
    long UserId,
    string DisplayName,
    WorkforceState WorkforceState,
    /// <summary>On the clock right now, in any of the on-shift states.</summary>
    bool IsOnShift,

    // What they are doing this minute.
    long? ActiveTaskId,
    string? ActiveTaskNumber,
    string? ActiveTaskTitle,
    /// <summary>How long the running timer has been going. Null when nothing is running.</summary>
    TimeSpan? ActiveFor,

    // What is already on them.
    /// <summary>Started and not finished.</summary>
    int ActiveCount,
    /// <summary>Theirs, but not started: assigned, ready, paused, blocked, waiting to be redone.</summary>
    int WaitingCount,
    /// <summary>Of those, how many are due before the end of today.</summary>
    int DueTodayCount,

    /// <summary>
    /// Work they have recently done in the same part of the product — same client, or same module.
    /// The one piece of context that is about fit rather than availability.
    /// </summary>
    IReadOnlyList<string> RecentRelated);

/// <summary>One assignee's load, for the workload/capacity view.</summary>
public sealed record WorkloadDto(
    long UserId,
    string DisplayName,
    WorkforceState WorkforceState,
    int OpenTaskCount,
    int InProgressCount,
    int BlockedCount,
    decimal EstimatedHoursOutstanding,
    long? ActiveTaskId,
    string? ActiveTaskNumber,
    /// <summary>
    /// On the clock right now. Answered here rather than by the client re-listing the on-shift
    /// states: two copies of the state machine is one too many, and this one would drift the first
    /// time a state was added.
    /// </summary>
    bool IsOnShift = false);
