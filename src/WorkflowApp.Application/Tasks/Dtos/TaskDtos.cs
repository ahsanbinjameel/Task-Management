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
    IReadOnlyList<string>? SupportPeople = null);

public sealed record StatusHistoryDto(
    long Id,
    WorkTaskStatus FromStatus,
    WorkTaskStatus ToStatus,
    long ChangedByUserId,
    DateTimeOffset ChangedAt,
    string? Reason,
    bool WasOverride);

public sealed record AssignmentHistoryDto(
    long Id,
    long? FromUserId,
    long? ToUserId,
    long AssignedByUserId,
    DateTimeOffset AssignedAt,
    string? Reason);

public sealed record TaskActivityDto(
    long Id,
    ActivityType Type,
    long ActorUserId,
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
    IReadOnlyList<AttachmentDto> Attachments);

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
    RequestContextDto? Request = null);

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
    string? ActiveTaskNumber);
