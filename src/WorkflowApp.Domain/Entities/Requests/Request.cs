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

    /// <summary>
    /// The batch this arrived in, if it arrived with others. Null for a request raised on its own,
    /// which stays the ordinary case — a batch is a convenience at intake, not a new kind of thing.
    /// </summary>
    public long? BatchId { get; set; }
    public RequestBatch? Batch { get; set; }

    /// <summary>Position within the batch, 1-based, so items read in the order they were typed.</summary>
    public int OrdinalInBatch { get; set; }

    public long RequestedByUserId { get; set; }
    public User RequestedByUser { get; set; } = default!;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? TargetDate { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Submitted;

    /// <summary>
    /// Set once approved and a task is generated.
    ///
    /// <b>Several requests may point at the same task.</b> That is how a reviewer folds three
    /// related items from one batch into a single piece of work without losing the fact that three
    /// separate things were asked for. It needs no join table: this column already answers "which
    /// task did my request become", and <c>WorkTask.RequestId</c> answers the other direction with
    /// the item the task was raised from. Both keep exactly one definition.
    /// </summary>
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
/// <summary>What a file is for, as opposed to what it is attached to.</summary>
public enum AttachmentKind
{
    /// <summary>Context: the requester's screenshots, a reference document. The default.</summary>
    General = 0,

    /// <summary>What the worker attached when marking the work finished.</summary>
    CompletionProof = 1,

    /// <summary>What the checker attached to a quality-check verdict.</summary>
    QCEvidence = 2,

    /// <summary>
    /// What an investigator attached to a verification: the screenshot of the wrong tax figure,
    /// the log extract. Distinct from <see cref="QCEvidence"/> because it justifies a finding about
    /// whether a problem exists, not a verdict on whether finished work is acceptable.
    /// </summary>
    VerificationEvidence = 3,
}

public class Attachment : BaseEntity
{
    public string OriginalFileName { get; set; } = default!;
    public string StoredPath { get; set; } = default!;       // relative to configured storage root
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = default!;

    public long UploadedByUserId { get; set; }

    // Polymorphic owner: an attachment belongs to a request, a task, a batch, or a verification —
    // exactly one. The batch case exists so the screenshot showing all eight problems is uploaded
    // once rather than once per item; the verification case so a checker's evidence stays with the
    // investigation rather than being filed against a task that may never exist.
    public long? RequestId { get; set; }
    public long? TaskId { get; set; }
    public long? BatchId { get; set; }
    public long? VerificationId { get; set; }

    /// <summary>
    /// Why this file is here. The owner says what it is attached to; this says what it is *for*.
    ///
    /// Without it, the screenshot a requester supplied to describe a problem and the screenshot a
    /// worker supplied to prove they fixed it are the same row in the same list, and the one thing
    /// anybody wants to know — "show me the evidence this was actually done" — cannot be asked.
    /// </summary>
    public AttachmentKind Kind { get; set; } = AttachmentKind.General;

    /// <summary>
    /// The quality-check attempt this evidence belongs to, for <see cref="AttachmentKind.QCEvidence"/>.
    ///
    /// Attempts are numbered and append-only: attempt 1 failed with one set of screenshots and
    /// attempt 2 passed with another, and a reader looking at attempt 1 must see attempt 1's. Null
    /// while the evidence has been uploaded but the verdict has not yet been submitted.
    /// </summary>
    public long? QCReviewId { get; set; }
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
