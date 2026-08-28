using System.ComponentModel.DataAnnotations;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Dtos;

/// <summary>One QC verdict against one acceptance criterion, as submitted by the reviewer.</summary>
public sealed record AcceptanceCriterionVerdictDto
{
    /// <summary>Zero-based position in the task's acceptance-criteria list.</summary>
    [Range(0, 500)]
    public int Index { get; init; }

    /// <summary>
    /// The reviewer's verdict: <c>true</c> passed, <c>false</c> failed, <c>null</c> not applicable.
    ///
    /// Tri-state on purpose. A criterion that does not apply to this piece of work is a legitimate
    /// answer — it is not the same as leaving it unanswered, and it must not block a pass. Only an
    /// explicit <c>false</c> does that.
    /// </summary>
    public bool? Met { get; init; }

    [MaxLength(1000)]
    public string? Note { get; init; }
}

public sealed record SubmitQCReviewDto
{
    [Required]
    public QCResult Result { get; init; }

    [MaxLength(4000)]
    public string? Comments { get; init; }

    [MaxLength(200)]
    public string? Environment { get; init; }

    [MaxLength(100)]
    public string? BuildVersion { get; init; }

    public IReadOnlyList<AcceptanceCriterionVerdictDto> Criteria { get; init; } =
        Array.Empty<AcceptanceCriterionVerdictDto>();
}

/// <summary>A criterion paired with the most recent verdict recorded against it, if any.</summary>
public sealed record AcceptanceCriterionDto(int Index, string Text, bool? Met, string? Note);

public sealed record AcceptanceCriteriaDto(
    long TaskId,
    IReadOnlyList<AcceptanceCriterionDto> Criteria,
    int? EvaluatedInAttempt,
    DateTimeOffset? EvaluatedAt);

public sealed record QCReviewDto(
    long Id,
    long TaskId,
    int AttemptNumber,
    long ReviewerUserId,
    string? ReviewerDisplayName,
    DateTimeOffset ReviewedAt,
    QCResult Result,
    string? Comments,
    string? Environment,
    string? BuildVersion,
    IReadOnlyList<AcceptanceCriterionDto> Criteria,
    /// <summary>
    /// What the checker attached to *this attempt*. Per attempt rather than per task, because
    /// attempts are append-only: the pictures that justified a failure must stay with the failure
    /// when a later attempt passes.
    /// </summary>
    IReadOnlyList<Requests.Dtos.AttachmentDto>? Attachments = null);

/// <summary>One closure precondition and whether the task currently satisfies it.</summary>
public sealed record ClosureRequirementDto(string Code, string Description, bool IsMet, string? Detail);

public sealed record ClosureChecklistDto(
    long TaskId,
    bool IsReady,
    IReadOnlyList<ClosureRequirementDto> Requirements,

    // ---- the acceptance policy (PRODUCT-CORE §7, invariant §4.14) --------------------------------
    //
    // Deliberately reported *beside* the requirements rather than as one of them. Requester
    // acceptance is a policy about a kind of work, not a universal invariant: a coordinator whose
    // requester has gone quiet must still be able to close, and turning this into an unmet
    // requirement would have forced them through the override path for an ordinary Tuesday.
    // So the screen is told, and the person decides.
    /// <summary>Whether someone asked for this work and can therefore confirm the fix.</summary>
    bool RequiresRequesterAcceptance = false,
    /// <summary>Who that is, so the button can name them instead of saying "the requester".</summary>
    string? RequesterDisplayName = null,
    /// <summary>True once the work is closed — acceptance and closure are the same act.</summary>
    bool RequesterHasConfirmed = false);

/// <summary>
/// The requester's "It's fixed". Carries nothing mandatory: they have already said the only thing
/// that matters by pressing it, and demanding a sentence to close your own request is the kind of
/// friction that sends people back to WhatsApp.
/// </summary>
public sealed record AcceptFixDto
{
    [MaxLength(2000)]
    public string? Note { get; init; }
}

/// <summary>
/// The requester's "Still not fixed". The reason is mandatory, because "it's still broken" with no
/// detail costs the worker exactly the round-trip this screen exists to remove.
/// </summary>
public sealed record RejectFixDto
{
    [Required]
    [MaxLength(2000)]
    public string Reason { get; init; } = default!;
}

public sealed record CloseTaskDto
{
    /// <summary>Recorded on the task if supplied. Closure requires a resolution one way or another.</summary>
    [MaxLength(4000)]
    public string? Resolution { get; init; }

    [MaxLength(1000)]
    public string? Reason { get; init; }
}
