using System.ComponentModel.DataAnnotations;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Dtos;

/// <summary>
/// Starting quick work asks for one thing. That is the point of it: anything more and the person
/// answering the phone will not bother, and the time will be lost instead of recorded.
/// </summary>
public sealed record StartQuickWorkDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; init; } = default!;

    /// <summary>
    /// Who it was for, typed rather than picked. Matched case- and space-insensitively against the
    /// names already in use, and created the first time it is seen — the same rule as the request
    /// form, so the two cannot fork the same client into two records.
    /// </summary>
    [MaxLength(200)]
    public string? ClientName { get; init; }

    /// <summary>
    /// The reason the running task is being put down, if one is running. Only its <c>IsBlocker</c>
    /// half is ignored: an interruption never means the task itself cannot proceed.
    /// </summary>
    public long? PauseReasonId { get; init; }
}

public sealed record FinishQuickWorkDto
{
    /// <summary>What came of it. Required — quick work with no outcome is a gap in the day's record.</summary>
    [Required]
    [MaxLength(2000)]
    public string Outcome { get; init; } = default!;

    /// <summary>
    /// Pick the interrupted task back up in the same operation. Defaults to true: handing the work
    /// back is what the person was going to do anyway, and making them find the task again is how
    /// an afternoon quietly loses twenty minutes.
    /// </summary>
    public bool ResumeInterruptedTask { get; init; } = true;
}

/// <summary>
/// Raising a request out of quick work that turned out to be real. Deliberately produces a
/// <b>request</b> and nothing more: a request never auto-becomes a task, and ten minutes on the
/// phone is not a review.
/// </summary>
public sealed record PromoteQuickWorkDto
{
    [MaxLength(200)]
    public string? Title { get; init; }

    [Required]
    [MaxLength(4000)]
    public string Description { get; init; } = default!;

    public RequestType Type { get; init; } = RequestType.Support;
    public RequestedUrgency RequestedUrgency { get; init; } = RequestedUrgency.Normal;
}

public sealed record QuickWorkDto(
    long Id,
    string Title,
    long UserId,
    string? UserDisplayName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    /// <summary>How long it ran. Still climbing while it is active.</summary>
    TimeSpan Duration,
    QuickWorkStatus Status,
    long? ClientId,
    string? ClientName,
    string? Outcome,
    /// <summary>The task it displaced, named so the screen can offer to hand the work back.</summary>
    long? InterruptedTaskId,
    string? InterruptedTaskNumber,
    long? PromotedToRequestId,
    string? PromotedToRequestNumber);
