using System.ComponentModel.DataAnnotations;
using WorkflowApp.Application.Requests.Dtos;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Verifications.Dtos;

/// <summary>
/// Raising a check on something. Nothing here requires a request or a task to exist — an
/// independent verification is the ordinary case, not a special one.
/// </summary>
public sealed record CreateVerificationDto
{
    [Required, MaxLength(300)]
    public string Title { get; init; } = default!;

    /// <summary>What the checker is being asked to do.</summary>
    [MaxLength(4000)]
    public string? Instructions { get; init; }

    /// <summary>What the thing should do, so "correct" is not the checker's guess.</summary>
    [MaxLength(2000)]
    public string? ExpectedBehavior { get; init; }

    public VerificationTargetType TargetType { get; init; } = VerificationTargetType.Other;

    /// <summary>Required when the target is a module; ignored otherwise.</summary>
    public long? ModuleId { get; init; }

    /// <summary>The form, screen or report by name, where no row in this database represents it.</summary>
    [MaxLength(300)]
    public string? TargetName { get; init; }

    /// <summary>A build version, an environment, a URL — whatever pins the target down.</summary>
    [MaxLength(300)]
    public string? TargetReference { get; init; }

    public Priority Priority { get; init; } = Priority.Normal;

    /// <summary>
    /// Who should look at it. Optional: a verification may be raised and given out afterwards, in
    /// which case it waits in <see cref="VerificationStatus.Requested"/>.
    /// </summary>
    public long? AssignToUserId { get; init; }
}

/// <summary>Giving it to a checker, or moving it to a different one.</summary>
public sealed record AssignVerificationDto
{
    [Required]
    public long AssignToUserId { get; init; }

    /// <summary>
    /// Mandatory when taking it off somebody who already had it — the same rule the task side
    /// applies to reassignment. Ignored on a first assignment, where there is nothing to explain.
    /// </summary>
    [MaxLength(2000)]
    public string? Reason { get; init; }
}

/// <summary>What the checker found. The only way a verification reaches Completed.</summary>
public sealed record RecordVerificationResultDto
{
    [Required]
    public VerificationResult Result { get; init; }

    /// <summary>
    /// Required. A verdict with no account of how it was reached leaves the reviewer exactly where
    /// they started, which is the problem this whole feature exists to solve.
    /// </summary>
    [Required, MaxLength(8000)]
    public string Findings { get; init; } = default!;
}

public sealed record CancelVerificationDto
{
    [Required, MaxLength(2000)]
    public string Reason { get; init; } = default!;
}

/// <summary>Routing a request to a checker instead of deciding it. Carried inside the triage DTO.</summary>
public sealed record SendForVerificationDto
{
    /// <summary>Defaults to the request's own title when left blank.</summary>
    [MaxLength(300)]
    public string? Title { get; init; }

    [MaxLength(4000)]
    public string? Instructions { get; init; }

    [MaxLength(2000)]
    public string? ExpectedBehavior { get; init; }

    /// <summary>Defaults to <see cref="VerificationTargetType.Request"/> — checking what was asked about.</summary>
    public VerificationTargetType TargetType { get; init; } = VerificationTargetType.Request;

    public long? ModuleId { get; init; }

    [MaxLength(300)]
    public string? TargetName { get; init; }

    [MaxLength(300)]
    public string? TargetReference { get; init; }

    public Priority? Priority { get; init; }

    public long? AssignToUserId { get; init; }
}

/// <summary>One readable line of what happened to a verification.</summary>
public sealed record VerificationActivityDto(
    long Id,
    string Type,
    long ActorUserId,
    string? ActorDisplayName,
    DateTimeOffset OccurredAt,
    string Description);

/// <summary>List-view projection — narrow, so queue screens stay cheap.</summary>
public sealed record VerificationSummaryDto(
    long Id,
    string VerificationNumber,
    string Title,
    VerificationStatus Status,
    /// <summary>The words to print for <see cref="Status"/>. Server-owned, so the client cannot drift.</summary>
    string StatusLabel,
    Priority Priority,
    VerificationTargetType TargetType,
    /// <summary>The target in one line, whichever kind it is.</summary>
    string TargetSummary,
    long RequestedByUserId,
    string RequestedByDisplayName,
    DateTimeOffset RequestedAt,
    long? AssignedToUserId,
    string? AssignedToDisplayName,
    VerificationResult? Result,
    string? ResultLabel,
    DateTimeOffset? CompletedAt,
    long? RequestId,
    string? RequestNumber,
    int AttachmentCount);

public sealed record VerificationDetailDto(
    long Id,
    string VerificationNumber,
    string Title,
    string? Instructions,
    string? ExpectedBehavior,
    VerificationStatus Status,
    string StatusLabel,
    Priority Priority,
    VerificationTargetType TargetType,
    string TargetSummary,
    long? ModuleId,
    string? ModuleName,
    string? TargetName,
    string? TargetReference,
    long RequestedByUserId,
    string RequestedByDisplayName,
    DateTimeOffset RequestedAt,
    long? AssignedToUserId,
    string? AssignedToDisplayName,
    long? AssignedByUserId,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    VerificationResult? Result,
    string? ResultLabel,
    string? Findings,
    string? CancellationReason,
    /// <summary>The request this was raised from, where it came through triage.</summary>
    long? RequestId,
    string? RequestNumber,
    string? RequestTitle,
    IReadOnlyList<VerificationActivityDto> Activity,
    IReadOnlyList<AttachmentDto> Attachments,
    /// <summary>The concurrency token, echoed back on assignment the way tasks do it.</summary>
    string? RowVersion);

/// <summary>
/// A verification as it appears on the request that spawned it, so a reviewer reading the request
/// does not have to go and find it. A summary, not a copy — the detail screen is one click away.
/// </summary>
public sealed record RequestVerificationDto(
    long Id,
    string VerificationNumber,
    VerificationStatus Status,
    string StatusLabel,
    long? AssignedToUserId,
    string? AssignedToDisplayName,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    VerificationResult? Result,
    string? ResultLabel,
    string? Findings);
