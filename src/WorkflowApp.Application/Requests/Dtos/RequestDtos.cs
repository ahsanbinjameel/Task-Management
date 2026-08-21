using System.ComponentModel.DataAnnotations;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Requests.Dtos;

public sealed record CreateRequestDto
{
    [Required, MaxLength(300)]
    public string Title { get; init; } = default!;

    [Required, MaxLength(8000)]
    public string Description { get; init; } = default!;

    public RequestType Type { get; init; } = RequestType.Support;

    /// <summary>What the requester is asking for. Advisory only — triage sets the real priority.</summary>
    public RequestedUrgency RequestedUrgency { get; init; } = RequestedUrgency.Normal;

    public long? ProjectId { get; init; }
    public long? ClientId { get; init; }
    public long? ModuleId { get; init; }

    [MaxLength(2000)] public string? BusinessImpact { get; init; }
    [MaxLength(2000)] public string? ExpectedResult { get; init; }
    [MaxLength(2000)] public string? CurrentResult { get; init; }
    [MaxLength(4000)] public string? ReproductionSteps { get; init; }

    public DateTimeOffset? TargetDate { get; init; }
}

public sealed record UpdateRequestDto
{
    [Required, MaxLength(300)]
    public string Title { get; init; } = default!;

    [Required, MaxLength(8000)]
    public string Description { get; init; } = default!;

    public RequestType Type { get; init; }
    public RequestedUrgency RequestedUrgency { get; init; }

    [MaxLength(2000)] public string? BusinessImpact { get; init; }
    [MaxLength(2000)] public string? ExpectedResult { get; init; }
    [MaxLength(2000)] public string? CurrentResult { get; init; }
    [MaxLength(4000)] public string? ReproductionSteps { get; init; }

    public DateTimeOffset? TargetDate { get; init; }
}

/// <summary>Triage outcomes a reviewer can record. Each maps to one <see cref="RequestStatus"/>.</summary>
public enum TriageOutcome
{
    Approve = 0,
    Reject = 1,
    RequestClarification = 2,
    MarkDuplicate = 3,
    Defer = 4,
    Escalate = 5
}

public sealed record TriageDecisionDto
{
    [Required]
    public TriageOutcome Outcome { get; init; }

    /// <summary>Mandatory for reject, clarification, duplicate and defer.</summary>
    [MaxLength(2000)]
    public string? Reason { get; init; }

    /// <summary>Required when the outcome is <see cref="TriageOutcome.MarkDuplicate"/>.</summary>
    public long? DuplicateOfRequestId { get; init; }

    /// <summary>The operative priority when approving. Defaults from the requested urgency.</summary>
    public Priority? ApprovedPriority { get; init; }

    public decimal? EstimatedEffortHours { get; init; }
    public DateTimeOffset? DueDate { get; init; }

    [MaxLength(4000)]
    public string? AcceptanceCriteria { get; init; }
}

public sealed record AnswerClarificationDto
{
    [Required, MaxLength(2000)]
    public string Answer { get; init; } = default!;
}

public sealed record AttachmentDto(
    long Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    long UploadedByUserId,
    DateTimeOffset UploadedAt);

public sealed record ClarificationDto(
    long Id,
    long AskedByUserId,
    string Question,
    DateTimeOffset AskedAt,
    long? AnsweredByUserId,
    string? Answer,
    DateTimeOffset? AnsweredAt);

/// <summary>List-view projection — deliberately narrow so queue screens stay cheap.</summary>
public sealed record RequestSummaryDto(
    long Id,
    string RequestNumber,
    string Title,
    RequestType Type,
    RequestStatus Status,
    RequestedUrgency RequestedUrgency,
    long RequestedByUserId,
    string RequestedByDisplayName,
    DateTimeOffset RequestedAt,
    DateTimeOffset? TargetDate,
    long? GeneratedTaskId,
    int AttachmentCount,
    bool HasOpenClarification);

public sealed record RequestDetailDto(
    long Id,
    string RequestNumber,
    string Title,
    string Description,
    RequestType Type,
    RequestStatus Status,
    RequestedUrgency RequestedUrgency,
    long? ProjectId,
    long? ClientId,
    long? ModuleId,
    string? BusinessImpact,
    string? ExpectedResult,
    string? CurrentResult,
    string? ReproductionSteps,
    long RequestedByUserId,
    string RequestedByDisplayName,
    DateTimeOffset RequestedAt,
    DateTimeOffset? TargetDate,
    long? RelatedRequestId,
    long? GeneratedTaskId,
    IReadOnlyList<ClarificationDto> Clarifications,
    IReadOnlyList<AttachmentDto> Attachments);
