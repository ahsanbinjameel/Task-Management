using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Domain.Entities.Requests;

/// <summary>
/// A submitted request — intake only. It is NOT executable work. Only after triage + approval
/// does the system create a Task. Rejected/duplicate requests never generate tasks.
/// </summary>
public class Request : BaseEntity
{
    public string RequestNumber { get; set; } = default!;   // e.g. REQ-000123

    public string Title { get; set; } = default!;
    public string Description { get; set; } = default!;
    public RequestType Type { get; set; }

    public long? ProjectId { get; set; }
    public long? ClientId { get; set; }
    public long? ModuleId { get; set; }

    public RequestedUrgency RequestedUrgency { get; set; } = RequestedUrgency.Normal;
    public string? BusinessImpact { get; set; }
    public string? ExpectedResult { get; set; }
    public string? CurrentResult { get; set; }
    public string? ReproductionSteps { get; set; }

    public long? RelatedRequestId { get; set; }

    public long RequestedByUserId { get; set; }
    public User RequestedByUser { get; set; } = default!;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? TargetDate { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Submitted;

    /// <summary>Set once approved and a task is generated.</summary>
    public long? GeneratedTaskId { get; set; }

    public ICollection<RequestClarification> Clarifications { get; set; } = new List<RequestClarification>();
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}

/// <summary>
/// One entry in the clarification thread. Kept append-only so the full back-and-forth
/// between reviewer and requester is preserved.
/// </summary>
public class RequestClarification : BaseEntity
{
    public long RequestId { get; set; }
    public Request Request { get; set; } = default!;

    public long AskedByUserId { get; set; }
    public string Question { get; set; } = default!;
    public DateTimeOffset AskedAt { get; set; } = DateTimeOffset.UtcNow;

    public long? AnsweredByUserId { get; set; }
    public string? Answer { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
}

/// <summary>
/// File metadata only — the binary lives on disk (configurable root). Access is always
/// through an authorized endpoint; the row records who/what/where for auditing.
/// </summary>
public class Attachment : BaseEntity
{
    public string OriginalFileName { get; set; } = default!;
    public string StoredPath { get; set; } = default!;       // relative to configured storage root
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = default!;

    public long UploadedByUserId { get; set; }

    // Polymorphic owner: an attachment belongs to a request OR a task (exactly one).
    public long? RequestId { get; set; }
    public long? TaskId { get; set; }
}

/// <summary>
/// What happened to a request, in words a requester can read.
///
/// Mirrors <c>TaskActivity</c>: requests had no history of their own, so an edit after submission
/// left no trace anyone could see. Deliberately separate from <c>AuditLog</c>, which records the
/// technical before/after for administrators — this is the human-readable stream.
/// </summary>
public class RequestActivity : BaseEntity
{
    public long RequestId { get; set; }
    public ActivityType Type { get; set; }
    public long ActorUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string Description { get; set; } = default!;
}
