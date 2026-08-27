using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Domain.Entities.Verifications;

/// <summary>
/// Assigned investigation: someone is asked to find out whether a thing actually works.
///
/// <para>
/// It exists because the system had no way to ask that question. A reviewer handed
/// "the salary form is not calculating tax correctly" can rarely tell from the words whether it is
/// a defect, a configuration mistake, bad data, a permission problem or a misunderstanding — and
/// the only route available was to approve it into a <see cref="Tasks.WorkTask"/>, which is to say
/// to commit the organisation to building something before anybody had established there was
/// anything to build.
/// </para>
///
/// <para>
/// <b>Not a <see cref="Tasks.QCReview"/>.</b> QC answers "does this finished work meet its
/// acceptance criteria?" and is bound to a task's lifecycle, its numbered attempts and its
/// segregation-of-duties rules. This answers "is there a problem here at all?", and needs no
/// completed task — usually there is no task, which is the entire point.
/// </para>
///
/// <para>
/// <b>Not <see cref="Tasks.QuickWork"/> either.</b> Quick work is unplanned work somebody picked up
/// themselves and is already doing; a verification is planned work one person deliberately assigns
/// to another. The two look similar only in that neither began as a request.
/// </para>
///
/// <para>
/// <b>It never creates work.</b> Whatever the checker finds, a confirmed issue returns the request
/// to a reviewer with the findings attached. Approving it remains a separate, explicit decision, so
/// <c>TaskCreationService</c> keeps the monopoly that makes "a request never auto-becomes a task"
/// auditable rather than hopeful.
/// </para>
/// </summary>
public class Verification : BaseEntity
{
    public string VerificationNumber { get; set; } = default!;   // e.g. VER-000123

    public string Title { get; set; } = default!;

    /// <summary>What the checker is being asked to do. The instruction, not the complaint.</summary>
    public string? Instructions { get; set; }

    /// <summary>What the thing is supposed to do, so "correct" is not left to the checker's guess.</summary>
    public string? ExpectedBehavior { get; set; }

    // --- where it came from -------------------------------------------------------------------

    /// <summary>
    /// The request that was routed here, if it came from triage. Null for a check somebody raised
    /// on its own — that is a first-class case, not a degenerate one.
    ///
    /// This is the <em>source</em>: it says what caused the check to be asked for, and it is the
    /// thing handed back when the answer arrives. What is being <em>checked</em> is
    /// <see cref="TargetType"/> and the columns under it, which usually but not always agree.
    /// </summary>
    public long? RequestId { get; set; }
    public Request? Request { get; set; }

    // --- what is being checked ----------------------------------------------------------------

    public VerificationTargetType TargetType { get; set; } = VerificationTargetType.Other;

    /// <summary>
    /// The module under test, for <see cref="VerificationTargetType.Module"/>. A real foreign key
    /// because <c>Module</c> is a real row — a loose id column pointing at "whatever TargetType
    /// says" would be unjoinable and unconstrained, and the first orphan would be silent.
    /// </summary>
    public long? ModuleId { get; set; }
    public Module? Module { get; set; }

    /// <summary>
    /// What is being checked, in words, where the thing is not an aggregate this database holds —
    /// a form, a screen, a report. Carried alongside the foreign keys rather than instead of them.
    /// </summary>
    public string? TargetName { get; set; }

    /// <summary>A build version, an environment, a URL — whatever pins the target down.</summary>
    public string? TargetReference { get; set; }

    // --- who and when -------------------------------------------------------------------------

    public long RequestedByUserId { get; set; }
    public User RequestedByUser { get; set; } = default!;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The checker. Null while it is still <see cref="VerificationStatus.Requested"/>.</summary>
    public long? AssignedToUserId { get; set; }
    public User? AssignedToUser { get; set; }

    public long? AssignedByUserId { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }

    public Priority Priority { get; set; } = Priority.Normal;
    public VerificationStatus Status { get; set; } = VerificationStatus.Requested;

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // --- what came of it ----------------------------------------------------------------------

    /// <summary>Set once, when the check reports. Null until then.</summary>
    public VerificationResult? Result { get; set; }

    /// <summary>What the checker found. Required to complete — a verdict with no account of how it
    /// was reached tells the reviewer nothing they can act on.</summary>
    public string? Findings { get; set; }

    /// <summary>Why it was called off. Only set for <see cref="VerificationStatus.Cancelled"/>.</summary>
    public string? CancellationReason { get; set; }

    public ICollection<VerificationActivity> Activity { get; set; } = new List<VerificationActivity>();
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}

/// <summary>
/// What happened to a verification, in readable words.
///
/// The third of the same shape, after <c>TaskActivity</c> and <c>RequestActivity</c>, and kept
/// separate for the same reason they are: this is the account a person reads, distinct from
/// <c>AuditLog</c>'s technical before-and-after.
/// </summary>
public class VerificationActivity : BaseEntity
{
    public long VerificationId { get; set; }
    public Verification Verification { get; set; } = default!;

    public ActivityType Type { get; set; }
    public long ActorUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string Description { get; set; } = default!;
}
