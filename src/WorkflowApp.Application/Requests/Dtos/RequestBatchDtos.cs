using System.ComponentModel.DataAnnotations;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Requests.Dtos;

/// <summary>
/// One line of a batch. Deliberately smaller than <see cref="CreateRequestDto"/>: the client is
/// shared by the batch, and asking somebody to fill in business impact and reproduction steps eight
/// times is how you get eight copies of "see above".
/// </summary>
public sealed record BatchItemDto
{
    [Required, MaxLength(300)]
    public string Title { get; init; } = default!;

    [Required, MaxLength(8000)]
    public string Description { get; init; } = default!;

    public RequestType Type { get; init; } = RequestType.Support;
    public RequestedUrgency RequestedUrgency { get; init; } = RequestedUrgency.Normal;

    public DateTimeOffset? TargetDate { get; init; }
}

public sealed record CreateRequestBatchDto
{
    /// <summary>What the whole batch is about — "Month-end problems", "New starter setup".</summary>
    [Required, MaxLength(300)]
    public string Title { get; init; } = default!;

    /// <summary>Context that applies to every item, written once.</summary>
    [MaxLength(4000)]
    public string? Note { get; init; }

    /// <summary>Shared, and copied onto each item so an item can be corrected on its own later.</summary>
    [MaxLength(200)]
    public string? ClientName { get; init; }

    [Required]
    [MinLength(1, ErrorMessage = "A batch needs at least one item.")]
    [MaxLength(50, ErrorMessage = "Fifty items is enough for one submission.")]
    public IReadOnlyList<BatchItemDto> Items { get; init; } = Array.Empty<BatchItemDto>();
}

/// <summary>
/// Approving several items of a batch as one piece of work.
///
/// The reviewer's judgement, not the system's: "these three are the same underlying fix" is a call
/// only a person can make. Every chosen item still gets its own approval and its own audit row —
/// they simply end up pointing at the same task.
/// </summary>
public sealed record ApproveTogetherDto
{
    [Required]
    [MinLength(1, ErrorMessage = "Choose at least one item.")]
    public IReadOnlyList<long> RequestIds { get; init; } = Array.Empty<long>();

    /// <summary>The title for the combined task. Defaults to the batch's title.</summary>
    [MaxLength(300)]
    public string? TaskTitle { get; init; }

    public Priority? ApprovedPriority { get; init; }
    public decimal? EstimatedEffortHours { get; init; }
    public DateTimeOffset? DueDate { get; init; }

    [MaxLength(4000)]
    public string? AcceptanceCriteria { get; init; }
}

/// <summary>One item of a batch, as a reviewer scanning the batch needs it.</summary>
public sealed record BatchItemSummaryDto(
    long Id,
    string RequestNumber,
    int Ordinal,
    string Title,
    RequestType Type,
    RequestedUrgency RequestedUrgency,
    RequestStatus Status,
    /// <summary>Plain-language status, from the same map the rest of the app uses.</summary>
    string StatusLabel,
    /// <summary>The work this item became, where it has become any.</summary>
    long? GeneratedTaskId,
    string? GeneratedTaskNumber,
    /// <summary>Other items of this batch that were folded into the same task.</summary>
    IReadOnlyList<string> SharedTaskWith);

public sealed record RequestBatchSummaryDto(
    long Id,
    string BatchNumber,
    string Title,
    string RequestedByDisplayName,
    DateTimeOffset RequestedAt,
    string? ClientName,
    int ItemCount,
    /// <summary>How many still need a triage decision. The only number a review queue needs.</summary>
    int AwaitingDecisionCount,
    int ApprovedCount,
    int DeclinedCount);

public sealed record RequestBatchDetailDto(
    long Id,
    string BatchNumber,
    string Title,
    string? Note,
    long RequestedByUserId,
    string RequestedByDisplayName,
    DateTimeOffset RequestedAt,
    long? ClientId,
    string? ClientName,
    IReadOnlyList<BatchItemSummaryDto> Items,
    IReadOnlyList<AttachmentDto> Attachments);
