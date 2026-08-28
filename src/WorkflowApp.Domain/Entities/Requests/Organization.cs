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

/// <summary>
/// A part of the product: Sales, Accounts, Inventory. The top of the product catalog.
///
/// <b>The catalog is client-independent, deliberately</b> (PRODUCT-CORE §5). Your product has
/// modules and forms; each client runs an instance of that product. Modelling
/// Client → Module → Form as one tree would give every client its own private copy of the same
/// form, and the questions worth asking would become unanswerable: "show me every Delivery Order
/// detail-report issue across all clients", "which forms generate the most support", "is this
/// posting bug unique to ABC or are four clients seeing it".
///
/// So a request ties together one point on each of two orthogonal axes:
/// <code>Request = Client? × ProductLocation(Module → Form → Surface)</code>
/// and internal work is simply the client axis left empty. There is no "Internal" client, and
/// there must never be one — <c>ClientId</c> is nullable, full stop.
///
/// <c>ProjectId</c> is vestigial. It predates this model and is the one link that could be read as
/// tying a module to a client (a Project carries a ClientId), so nothing in the catalog path sets
/// or reads it: <see cref="Form"/> hangs off the module, and the module picker has never filtered
/// by project. It is left in place rather than dropped because removing a column is a migration
/// that can only lose data, and it costs nothing where it is.
/// </summary>
public class Module : BaseEntity
{
    public string Name { get; set; } = default!;

    /// <summary>Vestigial — see the note on this class. Do not use it for the catalog.</summary>
    public long? ProjectId { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A screen or document within a module: Delivery Order, Sales Invoice, Accounts Posting.
///
/// Belongs to a <see cref="Module"/> and to nothing else. Never to a client.
/// </summary>
public class Form : BaseEntity
{
    public string Name { get; set; } = default!;

    public long ModuleId { get; set; }
    public Module Module { get; set; } = default!;

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A way of looking at a form: the form itself, its History, a Detail Report, a Master Report.
///
/// This is the grain most support conversations actually happen at — "the delivery order *detail
/// report* total is wrong" is a different problem from "the delivery order *form* will not save",
/// and the two go to different places in the code. It is the finest grain the catalog models, and
/// deliberately so: fields, controls, report columns, builds and versions are all imaginable and
/// none has been asked for.
/// </summary>
public class FormSurface : BaseEntity
{
    public string Name { get; set; } = default!;

    public long FormId { get; set; }
    public Form Form { get; set; } = default!;

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
