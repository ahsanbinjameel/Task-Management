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

    /// <summary>
    /// The point in full. Optional: the fast intake form collects one piece of text per point and
    /// sends its first line as the title, so demanding a second, longer version of the same
    /// sentence would be asking the requester to write it twice (PRODUCT-CORE §8). The service
    /// falls back to the title when it is absent.
    /// </summary>
    [MaxLength(8000)]
    public string? Description { get; init; }

    public RequestType Type { get; init; } = RequestType.Support;
    public RequestedUrgency RequestedUrgency { get; init; } = RequestedUrgency.Normal;

    public DateTimeOffset? TargetDate { get; init; }
}

public sealed record CreateRequestBatchDto
{
    /// <summary>
    /// What the whole batch is about — "Month-end problems", "New starter setup".
    ///
    /// <b>Optional.</b> It used to be required, which meant the intake form had to ask the
    /// requester to name their own submission before they could describe a single problem
    /// (PRODUCT-CORE §8). Nobody asking about a broken invoice thinks of it as a batch, and the
    /// answer was invariably a restatement of the first point. When it is absent the service names
    /// the batch from what was actually said.
    /// </summary>
    [MaxLength(300)]
    public string? Title { get; init; }

    /// <summary>Context that applies to every item, written once.</summary>
    [MaxLength(4000)]
    public string? Note { get; init; }

    /// <summary>Shared, and copied onto each item so an item can be corrected on its own later.</summary>
    [MaxLength(200)]
    public string? ClientName { get; init; }

    /// <summary>
    /// Where in the product these are, if the requester happens to know (PRODUCT-CORE §5, §8).
    /// Shared defaults, copied onto each item for the same reason the client is.
    ///
    /// Optional and never demanded: intake asks for a client and a sentence, and placing the work
    /// precisely is a triage concern. Four mandatory dropdowns in front of somebody reporting a
    /// broken invoice is how you stop them reporting it.
    /// </summary>
    public long? ModuleId { get; init; }
    public long? FormId { get; init; }

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
