using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Domain.Entities.Workforce;

/// <summary>
/// One shift/attendance session per employee per working day. Distinct from the auth session
/// (a user stays authenticated across breaks) and from task work sessions.
/// </summary>
public class ShiftSession : BaseEntity
{
    public long UserId { get; set; }
    public User User { get; set; } = default!;

    public DateTimeOffset ShiftStart { get; set; }
    public DateTimeOffset? ShiftEnd { get; set; }

    public string? StartDeviceInfo { get; set; }
    public string? StartIpAddress { get; set; }

    /// <summary>Set true when a shift is closed by cleanup rather than an explicit End Shift.</summary>
    public bool EndedImproperly { get; set; }

    /// <summary>
    /// Who closed the shift. Null when the employee ended it themselves; set when a supervisor
    /// force-ended it. Automatic cleanup leaves it null and sets <see cref="EndedImproperly"/>.
    /// </summary>
    public long? EndedByUserId { get; set; }

    /// <summary>Free-text note captured at close — mandatory when a supervisor force-ends a shift.</summary>
    public string? EndNote { get; set; }

    public ICollection<ActivityEvent> Events { get; set; } = new List<ActivityEvent>();
}

/// <summary>
/// Append-only timeline of workforce events. Powers the daily timeline and reports, e.g.
/// "10:01 Shift Started", "13:02 Lunch Started". Task events (started/paused) are also
/// echoed here so a single ordered daily timeline can be produced.
/// </summary>
public class ActivityEvent : BaseEntity
{
    public long UserId { get; set; }
    public User User { get; set; } = default!;

    public long? ShiftSessionId { get; set; }
    public ShiftSession? ShiftSession { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The workforce state this event moved the user into (if any).</summary>
    public WorkforceState? ResultingState { get; set; }

    /// <summary>Human-readable label, e.g. "Lunch Started", "Task TSK-120 Started".</summary>
    public string Label { get; set; } = default!;

    /// <summary>Optional link to a task this event concerns.</summary>
    public long? RelatedTaskId { get; set; }
    public string? Note { get; set; }
}
