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

    /// <summary>
    /// Who the work is for, typed rather than picked. The name is matched against the ones already
    /// in use and created the first time it is seen, so the list builds itself and nobody has to
    /// maintain a client register. Blank means internal work.
    /// </summary>
    [MaxLength(200)]
    public string? ClientName { get; init; }

    /// <summary>
    /// Where in the product this is, if the requester happens to know (PRODUCT-CORE §5, §8).
    ///
    /// Optional and never demanded. Intake asks for a client and a sentence; placing the work
    /// precisely is a triage concern, because that is where somebody who knows the system is
    /// looking at it. Surface is deliberately absent here — it is finer than anyone reporting a
    /// problem should be asked for.
    /// </summary>
    public long? ModuleId { get; init; }
    public long? FormId { get; init; }

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

    /// <summary>Null leaves the client alone; empty clears it.</summary>
    [MaxLength(200)]
    public string? ClientName { get; init; }

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
    Escalate = 5,

    /// <summary>
    /// Route it to a checker to establish whether there is really a problem, before deciding
    /// anything. Creates a <c>Verification</c> and <b>never</b> a task: the request comes back to
    /// review with the findings attached, and approving it remains a separate, explicit act.
    /// </summary>
    SendForVerification = 6
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

    /// <summary>
    /// How the work is classified. Set here rather than at intake: the requester knows what is
    /// wrong, not whether it is a defect, a change request or a configuration mistake, and a guess
    /// from them is a wrong label on every report that groups by it.
    ///
    /// Null leaves whatever the request already has.
    /// </summary>
    public RequestType? Type { get; init; }

    /// <summary>
    /// Who the work is for, if the requester did not say or got it wrong. Written to the request
    /// first and inherited by the task from there, so the two can never disagree about the client.
    /// Leave null to keep whatever the request already has.
    /// </summary>
    [MaxLength(200)]
    public string? ClientName { get; init; }

    // Where in the product this actually is (PRODUCT-CORE §5, §12D). This is a triage concern by
    // design: the requester gives rough input — a client and a sentence — and the reviewer refines
    // it into structured product context. Forcing four dropdowns on the person reporting a problem
    // is how you stop them reporting problems.
    //
    // Each is written to the request and inherited by the task from there, the same way the client
    // already is, so the two can never disagree. Null means "leave whatever the request has".
    public long? ModuleId { get; init; }
    public long? FormId { get; init; }
    public long? FormSurfaceId { get; init; }

    public decimal? EstimatedEffortHours { get; init; }
    public DateTimeOffset? DueDate { get; init; }

    [MaxLength(4000)]
    public string? AcceptanceCriteria { get; init; }

    /// <summary>
    /// Required when the outcome is <see cref="TriageOutcome.SendForVerification"/>. Everything
    /// about the check lives in its own shape rather than being smeared across the fields above,
    /// which all mean something else.
    /// </summary>
    public Verifications.Dtos.SendForVerificationDto? Verification { get; init; }
}

/// <summary>
/// A point found in a later round of testing against work already in hand (PRODUCT-CORE §6).
///
/// It becomes a request of its own, with its own number and its own triage decision. It carries the
/// shared client and product location so nobody retypes them, and it does <b>not</b> touch the
/// finish line of whatever is already running.
/// </summary>
public sealed record CreateFollowUpDto
{
    [Required, MaxLength(300)]
    public string Title { get; init; } = default!;

    [MaxLength(8000)]
    public string? Description { get; init; }

    public RequestType? Type { get; init; }
    public RequestedUrgency? RequestedUrgency { get; init; }
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

/// <summary>One readable line of what happened to a request.</summary>
public sealed record RequestActivityDto(
    long Id,
    string Type,
    long ActorUserId,
    string? ActorDisplayName,
    DateTimeOffset OccurredAt,
    string Description);

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
    bool HasOpenClarification,
    long? ClientId = null,
    string? ClientName = null,

    // ---- progress, so the requester never has to open the task ------------------------------
    //
    // A request stops moving the moment it is approved; everything after that happens on the task
    // it generated. Carrying the task's state back onto the request row is what lets someone who
    // asked for the work see what is happening to it without learning that "a task" exists.
    /// <summary>The generated task's internal status, where there is one.</summary>
    WorkTaskStatus? TaskStatus = null,
    /// <summary>The audience-facing status: what the reader should actually be told.</summary>
    string ViewKey = "",
    string ViewLabel = "",
    string? ResponsibleDisplayName = null,
    int ProgressPercent = 0,
    /// <summary>Last movement on either the request or its task — the "Updated" column.</summary>
    DateTimeOffset? UpdatedAt = null);

/// <summary>
/// What happened to a request after it was approved, said in the requester's language.
///
/// This exists so that "open the task to find out" stops being an instruction anyone has to
/// follow. Everything here is read off the generated task; none of it is stored twice.
/// </summary>
public sealed record RequestProgressDto(
    long TaskId,
    string TaskNumber,
    WorkTaskStatus TaskStatus,
    /// <summary>The audience-facing status — the same words the list showed.</summary>
    string StatusKey,
    string StatusLabel,
    string? ResponsibleDisplayName,
    IReadOnlyList<string> SupportPeople,
    int ProgressPercent,
    TimeSpan TotalWorkedTime,
    DateTimeOffset? StartedAt,
    DateTimeOffset? DueDate,
    /// <summary>Where the work has got to, in the worker's own words. Null until someone says.</summary>
    string? LatestUpdate,
    string? LatestUpdateBy,
    DateTimeOffset? LatestUpdateAt,
    /// <summary>Plain sentence about the quality check, rather than an attempt count.</summary>
    string QualityCheck,
    /// <summary>Why it is not moving, where that is the case.</summary>
    string? WaitingReason);

public sealed record RequestDetailDto(
    long Id,
    string RequestNumber,
    string Title,
    string Description,
    RequestType Type,
    RequestStatus Status,
    RequestedUrgency RequestedUrgency,
    long? ClientId,
    string? ClientName,
    string? BusinessImpact,
    string? ExpectedResult,
    string? CurrentResult,
    string? ReproductionSteps,
    long RequestedByUserId,
    string RequestedByDisplayName,
    DateTimeOffset RequestedAt,
    DateTimeOffset? TargetDate,
    /// <summary>
    /// The other axis (PRODUCT-CORE §5): "Sales · Delivery Order · Detail Report". Null until
    /// somebody places it — most requests arrive with none of it, and triage fills it in.
    /// </summary>
    string? ProductLocation,
    long? RelatedRequestId,
    /// <summary>The number of the request this came out of, when it is a later round.</summary>
    string? RelatedRequestNumber,
    /// <summary>Which round of testing found this. 1 for anything raised on its own.</summary>
    int Round,
    long? GeneratedTaskId,
    IReadOnlyList<RequestActivityDto> Activity,
    IReadOnlyList<ClarificationDto> Clarifications,
    IReadOnlyList<AttachmentDto> Attachments,

    /// <summary>
    /// The checks raised against this request, newest first. Empty for the ordinary request that
    /// never needed one. A summary, not a copy: the findings are here because that is the whole
    /// point of reading them, but everything else is one click away on the verification itself.
    /// </summary>
    IReadOnlyList<Verifications.Dtos.RequestVerificationDto> Verifications,

    /// <summary>The status this reader should be told, folding in the task where there is one.</summary>
    string ViewKey = "",
    string ViewLabel = "",
    /// <summary>Null until the request has been approved and work exists.</summary>
    RequestProgressDto? Progress = null,
    /// <summary>
    /// The submission this arrived in, when it was asked for alongside others. Carried so a
    /// requester who raised eight things at once can get back to the other seven.
    /// </summary>
    long? BatchId = null,
    string? BatchNumber = null,
    /// <summary>Which of the batch this was, 1-based. Zero for a request raised on its own.</summary>
    int OrdinalInBatch = 0,
    int BatchItemCount = 0);
