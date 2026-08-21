using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Domain.Entities.Tasks;

public class TaskComment : BaseEntity
{
    public long TaskId { get; set; }
    public long AuthorUserId { get; set; }
    public CommentCategory Category { get; set; } = CommentCategory.General;
    public string Body { get; set; } = default!;

    /// <summary>Whether the requester can see this. Internal/technical notes default to false.</summary>
    public bool VisibleToRequester { get; set; }
}

public class TaskDependency : BaseEntity
{
    public long TaskId { get; set; }               // this task ...
    public long RelatedTaskId { get; set; }        // ... relates to this one
    public DependencyType Type { get; set; }
}

/// <summary>Records scope changes so poor estimates are distinguishable from scope creep.</summary>
public class ScopeChange : BaseEntity
{
    public long TaskId { get; set; }
    public long RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Description { get; set; } = default!;
    public string? Reason { get; set; }
    public decimal? EstimatedImpactHours { get; set; }
    public DateTimeOffset? DeadlineImpact { get; set; }

    public long? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}

public class Notification : BaseEntity
{
    public long RecipientUserId { get; set; }
    public string Title { get; set; } = default!;
    public string? Body { get; set; }
    public string? LinkEntityType { get; set; }    // "Task" | "Request"
    public long? LinkEntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>
/// Technical/security audit stream, separate from the business activity timeline.
/// Append-only; admins must not be able to silently delete records.
/// </summary>
public class AuditLog : BaseEntity
{
    public long? ActorUserId { get; set; }
    public string Action { get; set; } = default!;         // "Login", "PermissionChanged", "Override"...
    public string? EntityType { get; set; }
    public long? EntityId { get; set; }
    public string? PreviousValues { get; set; }            // JSON
    public string? NewValues { get; set; }                 // JSON
    public string? IpAddress { get; set; }
    public string? DeviceInfo { get; set; }
}
