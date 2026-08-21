using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Domain.Entities.Tasks;

/// <summary>
/// One work session per start/resume. Pause/stop closes it. A task accumulates many sessions;
/// total work time is the sum of their durations. This replaces a single Start/End field.
/// </summary>
public class WorkSession : BaseEntity
{
    public long TaskId { get; set; }
    public WorkTask Task { get; set; } = default!;

    public long UserId { get; set; }
    public User User { get; set; } = default!;

    public DateTimeOffset SessionStart { get; set; }
    public DateTimeOffset? SessionEnd { get; set; }
    public WorkSessionStatus Status { get; set; } = WorkSessionStatus.Active;

    /// <summary>Why the session ended (pause reason). Null while active.</summary>
    public long? EndPauseReasonId { get; set; }
    public string? EndComment { get; set; }

    /// <summary>Set when this session was closed by an emergency interruption.</summary>
    public bool EndedByInterruption { get; set; }
    public long? InterruptedByTaskId { get; set; }

    public TimeSpan? Duration => SessionEnd.HasValue ? SessionEnd.Value - SessionStart : null;
}

public class QCReview : BaseEntity
{
    public long TaskId { get; set; }
    public WorkTask Task { get; set; } = default!;

    public long ReviewerUserId { get; set; }
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;

    public QCResult Result { get; set; }
    public string? Comments { get; set; }
    public string? AcceptanceCriteriaResults { get; set; }
    public string? Environment { get; set; }
    public string? BuildVersion { get; set; }

    /// <summary>Sequential attempt number so every QC pass/fail is retained.</summary>
    public int AttemptNumber { get; set; }
}

/// <summary>Immutable record of each assignment / reassignment.</summary>
public class AssignmentHistory : BaseEntity
{
    public long TaskId { get; set; }
    public long? FromUserId { get; set; }
    public long? ToUserId { get; set; }
    public long AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Reason { get; set; }
}

/// <summary>Immutable record of each status transition, with the reason where required.</summary>
public class StatusHistory : BaseEntity
{
    public long TaskId { get; set; }
    public WorkTaskStatus FromStatus { get; set; }
    public WorkTaskStatus ToStatus { get; set; }
    public long ChangedByUserId { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Reason { get; set; }
    public bool WasOverride { get; set; }
}

/// <summary>Human-readable business timeline shown inside the task.</summary>
public class TaskActivity : BaseEntity
{
    public long TaskId { get; set; }
    public ActivityType Type { get; set; }
    public long ActorUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string Description { get; set; } = default!;
}
