using System.ComponentModel.DataAnnotations;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Tasks.Dtos;

/// <summary>One QC verdict against one acceptance criterion, as submitted by the reviewer.</summary>
public sealed record AcceptanceCriterionVerdictDto
{
    /// <summary>Zero-based position in the task's acceptance-criteria list.</summary>
    [Range(0, 500)]
    public int Index { get; init; }

    public bool Met { get; init; }

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
    IReadOnlyList<AcceptanceCriterionDto> Criteria);

/// <summary>One closure precondition and whether the task currently satisfies it.</summary>
public sealed record ClosureRequirementDto(string Code, string Description, bool IsMet, string? Detail);

public sealed record ClosureChecklistDto(
    long TaskId,
    bool IsReady,
    IReadOnlyList<ClosureRequirementDto> Requirements);

public sealed record CloseTaskDto
{
    /// <summary>Recorded on the task if supplied. Closure requires a resolution one way or another.</summary>
    [MaxLength(4000)]
    public string? Resolution { get; init; }

    [MaxLength(1000)]
    public string? Reason { get; init; }
}
