using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Requests;

namespace WorkflowApp.Domain.Entities.Tasks;

/// <summary>Where a piece of quick work got to.</summary>
public enum QuickWorkStatus
{
    /// <summary>Running now. At most one per person, enforced by a filtered unique index.</summary>
    Active = 0,

    /// <summary>Done. The time is recorded and counts towards the day.</summary>
    Finished = 1,

    /// <summary>Started by mistake, or overtaken. Recorded, but not counted as productive.</summary>
    Cancelled = 2,
}

/// <summary>
/// Work that arrived without a request: the phone call, the person at the desk, the five minutes
/// that turned into forty.
///
/// It exists because the alternative is worse. Making someone raise a request, get it reviewed and
/// have a task created before they can answer a colleague's question means either the question
/// goes unanswered or — far more often — the time simply goes unrecorded, and the day's report
/// shows six hours of work in an eight-hour shift with no explanation. This is the smallest thing
/// that captures it: a title, a clock, and an outcome.
///
/// Deliberately <b>not</b> a <see cref="WorkTask"/>. A task carries a lifecycle, an assignee, a
/// quality check and a closure checklist, and every one of those would have to be given a
/// meaningless answer here. It is also deliberately not exempt from the rules: starting one pauses
/// whatever task was running, through the same interruption path a second task would use, so
/// "one thing at a time" still holds and the interrupted work keeps its recorded time.
///
/// <para>
/// Quick work can be <i>promoted</i>, and promotion creates a <see cref="Request"/> — never a task
/// directly. Ten minutes on the phone that turns out to be a fortnight's work still has to be
/// reviewed and approved like anything else; the promotion carries the context across so nobody
/// retypes it, and stops there.
/// </para>
/// </summary>
public class QuickWork : BaseEntity
{
    /// <summary>The only thing required to start. Everything else can be filled in afterwards.</summary>
    public string Title { get; set; } = default!;

    public long UserId { get; set; }
    public User User { get; set; } = default!;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    public QuickWorkStatus Status { get; set; } = QuickWorkStatus.Active;

    /// <summary>Who it was for, where that is known. Blank means internal.</summary>
    public long? ClientId { get; set; }
    public Client? Client { get; set; }

    /// <summary>What came of it, written when it finishes. The report's whole value rests on this.</summary>
    public string? Outcome { get; set; }

    /// <summary>
    /// The task this displaced, so it can be handed back at the end.
    ///
    /// A sibling of <see cref="WorkSession.InterruptedByTaskId"/> rather than a reuse of it: that
    /// column is task-shaped, and pointing it at a quick-work id would make every reader of the
    /// work-session table wrong about what it holds.
    /// </summary>
    public long? InterruptedTaskId { get; set; }
    public WorkTask? InterruptedTask { get; set; }

    /// <summary>Set when this turned out to be real work and was raised as a request.</summary>
    public long? PromotedToRequestId { get; set; }
    public Request? PromotedToRequest { get; set; }
}
