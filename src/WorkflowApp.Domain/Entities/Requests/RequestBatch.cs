using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Entities.Identity;

namespace WorkflowApp.Domain.Entities.Requests;

/// <summary>
/// Several things asked for at once.
///
/// "Here are eight problems from month-end" is one conversation, and making somebody submit it as
/// eight unrelated forms — retyping the client, re-attaching the same screenshot, losing the fact
/// that they arrived together — is how a system trains people to batch things up in a single
/// free-text box instead. So the batch holds what the items share: the client, one note, and the
/// files.
///
/// <para>
/// What it deliberately does <b>not</b> hold is a status. A batch is a wrapper, not a unit of work:
/// each item is a full <see cref="Request"/> with its own number and its own triage decision, and a
/// reviewer can approve three, reject one and ask a question about the rest. A status on the batch
/// would have to be either a lie or a summary, and a summary is something a screen can compute.
/// </para>
///
/// <para>
/// Nothing about the workflow changes. A batch cannot become a task; its items become tasks, one at
/// a time or several folded into one, and only through triage approval.
/// </para>
/// </summary>
public class RequestBatch : BaseEntity
{
    /// <summary>e.g. BAT-000012. Its own counter — printed numbers must not share a sequence.</summary>
    public string BatchNumber { get; set; } = default!;

    /// <summary>What the whole batch is about. The items carry their own titles.</summary>
    public string Title { get; set; } = default!;

    /// <summary>Context that applies to every item, so it is written once rather than eight times.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Who the work is for. Copied onto each item at creation rather than read through the batch:
    /// an item can be corrected at triage without dragging its siblings with it, which is exactly
    /// what happens when eight month-end problems turn out to belong to two different clients.
    /// </summary>
    public long? ClientId { get; set; }
    public Client? Client { get; set; }

    public long RequestedByUserId { get; set; }
    public User RequestedByUser { get; set; } = default!;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Request> Items { get; set; } = new List<Request>();

    /// <summary>Files that belong to the whole batch — the screenshot showing all eight problems.</summary>
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}
