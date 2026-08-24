using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Domain.Entities.Requests;

public class Department : BaseEntity
{
    public string Name { get; set; } = default!;
    public bool IsActive { get; set; } = true;
}

public class Team : BaseEntity
{
    public string Name { get; set; } = default!;
    public long? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Client : BaseEntity
{
    public string Name { get; set; } = default!;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Project : BaseEntity
{
    public string Name { get; set; } = default!;
    public string? Code { get; set; }
    public long? ClientId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Module : BaseEntity
{
    public string Name { get; set; } = default!;
    public long? ProjectId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Configurable pause reasons (admin-managed). Some require a comment.</summary>
public class PauseReason : BaseEntity
{
    public string Name { get; set; } = default!;      // e.g. "Waiting for client"
    public bool RequiresComment { get; set; }

    /// <summary>
    /// Whether the <em>task</em> genuinely cannot move on. This is about the work, not the person:
    /// waiting on a client blocks the task; going to lunch does not, because the task is still
    /// claimed and will continue when the worker returns.
    /// </summary>
    public bool IsBlocker { get; set; }

    /// <summary>The small, user-facing grouping this reason belongs to.</summary>
    public PauseCategory Category { get; set; } = PauseCategory.Other;

    /// <summary>
    /// Where the <em>person</em> goes, if anywhere. Set for Break / Lunch / Meeting; null when the
    /// worker stays on shift and free to pick up other work — which is the case for every reason
    /// that is about the task rather than about them.
    ///
    /// Never <c>ShiftEnded</c>: only the end-shift operation may set that.
    /// </summary>
    public WorkforceState? AwayState { get; set; }

    public bool IsActive { get; set; } = true;
}
