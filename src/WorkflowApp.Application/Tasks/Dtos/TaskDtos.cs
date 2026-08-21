using System.ComponentModel.DataAnnotations;
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
    bool HasActiveSession);

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
    long? ProjectId,
    long? ClientId,
    long? ModuleId,
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
    IReadOnlyList<long> CollaboratorUserIds,
    IReadOnlyList<WorkSessionDto> WorkSessions,
    IReadOnlyList<StatusHistoryDto> StatusHistory,
    IReadOnlyList<AssignmentHistoryDto> AssignmentHistory,
    IReadOnlyList<TaskActivityDto> Activity,
    string? RowVersion);

public sealed record PauseReasonDto(long Id, string Name, bool RequiresComment, bool IsBlocker);

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
