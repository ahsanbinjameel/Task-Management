using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Domain.Entities.Tasks;

/// <summary>
/// The executable unit of work, created from an approved Request. Named WorkTask to avoid
/// clashing with System.Threading.Tasks.Task.
/// </summary>
public class WorkTask : BaseEntity
{
    public string TaskNumber { get; set; } = default!;      // e.g. TSK-000120

    public long? RequestId { get; set; }                    // provenance back to intake

    public string Title { get; set; } = default!;
    public string Description { get; set; } = default!;

    public long? ProjectId { get; set; }
    public long? ClientId { get; set; }
    public long? ModuleId { get; set; }
    public RequestType Type { get; set; }

    /// <summary>Priority approved at triage — the operative one for scheduling.</summary>
    public Priority Priority { get; set; } = Priority.Normal;

    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.Approved;

    public long? PrimaryAssigneeUserId { get; set; }
    public User? PrimaryAssigneeUser { get; set; }
    public long? ReviewerUserId { get; set; }
    public long? QCUserId { get; set; }

    public decimal? EstimatedEffortHours { get; set; }
    public DateTimeOffset? DueDate { get; set; }

    public string? AcceptanceCriteria { get; set; }
    public string? Resolution { get; set; }
    public int ProgressPercent { get; set; }

    /// <summary>Position in the assignee's ordered work queue (lower = sooner).</summary>
    public int QueueOrder { get; set; }

    // Subtask / parent relationship
    public long? ParentTaskId { get; set; }
    public WorkTask? ParentTask { get; set; }
    public ICollection<WorkTask> SubTasks { get; set; } = new List<WorkTask>();

    // Navigation
    public ICollection<TaskCollaborator> Collaborators { get; set; } = new List<TaskCollaborator>();
    public ICollection<WorkSession> WorkSessions { get; set; } = new List<WorkSession>();
    public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();
    public ICollection<TaskActivity> Activities { get; set; } = new List<TaskActivity>();
    public ICollection<QCReview> QCReviews { get; set; } = new List<QCReview>();
    public ICollection<AssignmentHistory> AssignmentHistory { get; set; } = new List<AssignmentHistory>();
    public ICollection<StatusHistory> StatusHistory { get; set; } = new List<StatusHistory>();
    public ICollection<TaskDependency> Dependencies { get; set; } = new List<TaskDependency>();
    public ICollection<ScopeChange> ScopeChanges { get; set; } = new List<ScopeChange>();
}

/// <summary>Supporting user on a task. Primary assignee keeps accountability.</summary>
public class TaskCollaborator
{
    public long TaskId { get; set; }
    public WorkTask Task { get; set; } = default!;
    public long UserId { get; set; }
    public User User { get; set; } = default!;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public long AddedByUserId { get; set; }
}
