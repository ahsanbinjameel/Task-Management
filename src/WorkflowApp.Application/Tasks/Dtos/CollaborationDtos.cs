using System.ComponentModel.DataAnnotations;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Dtos;

// --- comments -----------------------------------------------------------------------------

public sealed record AddCommentDto
{
    [Required, MaxLength(4000)]
    public string Body { get; init; } = default!;

    public CommentCategory Category { get; init; } = CommentCategory.General;

    /// <summary>
    /// Overrides the category's default visibility. Left null, the category decides — which is what
    /// stops an internal note reaching the requester because somebody forgot to tick a box.
    /// </summary>
    public bool? VisibleToRequester { get; init; }
}

public sealed record TaskCommentDto(
    long Id,
    long TaskId,
    long AuthorUserId,
    string? AuthorDisplayName,
    CommentCategory Category,
    string Body,
    bool VisibleToRequester,
    DateTimeOffset CreatedAt);

// --- dependencies -------------------------------------------------------------------------

public sealed record AddDependencyDto
{
    [Required]
    public long RelatedTaskId { get; init; }

    public DependencyType Type { get; init; } = DependencyType.DependsOn;
}

public sealed record TaskDependencyDto(
    long Id,
    long TaskId,
    long RelatedTaskId,
    string RelatedTaskNumber,
    string RelatedTaskTitle,
    WorkTaskStatus RelatedTaskStatus,
    DependencyType Type,
    // True when this edge is currently holding the task up.
    bool IsBlocking);

public sealed record TaskDependencyGraphDto(
    long TaskId,
    // Edges this task declared.
    IReadOnlyList<TaskDependencyDto> Outgoing,
    // Edges other tasks declared pointing at this one.
    IReadOnlyList<TaskDependencyDto> Incoming,
    bool IsBlocked,
    IReadOnlyList<string> BlockedBy);

// --- subtasks -----------------------------------------------------------------------------

public sealed record CreateSubtaskDto
{
    [Required, MaxLength(300)]
    public string Title { get; init; } = default!;

    [Required, MaxLength(8000)]
    public string Description { get; init; } = default!;

    /// <summary>Defaults to the parent's priority.</summary>
    public Priority? Priority { get; init; }

    public decimal? EstimatedEffortHours { get; init; }
    public DateTimeOffset? DueDate { get; init; }

    [MaxLength(4000)] public string? AcceptanceCriteria { get; init; }

    /// <summary>Optional: assign it straight away rather than sending it to the queue.</summary>
    public long? AssigneeUserId { get; init; }
}

// --- scope changes ------------------------------------------------------------------------

public sealed record RequestScopeChangeDto
{
    [Required, MaxLength(4000)]
    public string Description { get; init; } = default!;

    [MaxLength(2000)]
    public string? Reason { get; init; }

    /// <summary>Added to the estimate on approval. Negative narrows the scope.</summary>
    public decimal? EstimatedImpactHours { get; init; }

    /// <summary>Replaces the due date on approval.</summary>
    public DateTimeOffset? DeadlineImpact { get; init; }
}

public sealed record ScopeChangeDto(
    long Id,
    long TaskId,
    long RequestedByUserId,
    string? RequestedByDisplayName,
    DateTimeOffset RequestedAt,
    string Description,
    string? Reason,
    decimal? EstimatedImpactHours,
    DateTimeOffset? DeadlineImpact,
    long? ApprovedByUserId,
    DateTimeOffset? ApprovedAt);

// --- reopen -------------------------------------------------------------------------------

public sealed record ReopenTaskDto
{
    /// <summary>Mandatory. Reopening closed work always has to say what was wrong with it.</summary>
    [Required, MaxLength(1000)]
    public string Reason { get; init; } = default!;
}
