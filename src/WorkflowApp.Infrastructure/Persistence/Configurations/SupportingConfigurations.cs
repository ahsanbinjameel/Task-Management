using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Entities.Workforce;

namespace WorkflowApp.Infrastructure.Persistence.Configurations;

/// <summary>
/// Reference/lookup tables. Names are unique so imports and admin screens cannot create
/// near-duplicates that fragment reporting.
/// </summary>
public class OrganizationConfig :
    IEntityTypeConfiguration<Department>, IEntityTypeConfiguration<Team>,
    IEntityTypeConfiguration<Client>, IEntityTypeConfiguration<Project>,
    IEntityTypeConfiguration<Module>, IEntityTypeConfiguration<PauseReason>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Team> b)
    {
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.HasIndex(x => new { x.DepartmentId, x.Name }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Code).HasMaxLength(30);
        b.HasIndex(x => x.Name).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Code).HasMaxLength(30);
        b.HasIndex(x => new { x.ClientId, x.Name }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Module> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<PauseReason> b)
    {
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

public class WorkforceConfig : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> b)
    {
        b.Property(e => e.Label).HasMaxLength(300).IsRequired();
        b.Property(e => e.Note).HasMaxLength(1000);

        b.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.ShiftSession).WithMany(s => s.Events).HasForeignKey(e => e.ShiftSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // The daily timeline query: one user, ordered by time.
        b.HasIndex(e => new { e.UserId, e.OccurredAt });
    }
}

/// <summary>
/// Decimal columns need explicit precision — without it SQL Server defaults to decimal(18,2) and
/// EF Core emits a model warning. Effort in hours never needs more than two decimal places.
/// </summary>
public class EffortPrecisionConfig :
    IEntityTypeConfiguration<WorkTask>, IEntityTypeConfiguration<ScopeChange>
{
    public void Configure(EntityTypeBuilder<WorkTask> b) =>
        b.Property(t => t.EstimatedEffortHours).HasPrecision(9, 2);

    public void Configure(EntityTypeBuilder<ScopeChange> b)
    {
        b.Property(s => s.EstimatedImpactHours).HasPrecision(9, 2);
        b.Property(s => s.Description).HasMaxLength(2000).IsRequired();
        b.Property(s => s.Reason).HasMaxLength(1000);
        b.HasIndex(s => s.TaskId);
    }
}

public class TaskHistoryConfig :
    IEntityTypeConfiguration<StatusHistory>, IEntityTypeConfiguration<AssignmentHistory>,
    IEntityTypeConfiguration<TaskActivity>, IEntityTypeConfiguration<TaskComment>,
    IEntityTypeConfiguration<QCReview>, IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<StatusHistory> b)
    {
        b.Property(h => h.Reason).HasMaxLength(1000);
        b.HasIndex(h => new { h.TaskId, h.ChangedAt });
    }

    public void Configure(EntityTypeBuilder<AssignmentHistory> b)
    {
        b.Property(h => h.Reason).HasMaxLength(1000);
        b.HasIndex(h => new { h.TaskId, h.AssignedAt });
    }

    public void Configure(EntityTypeBuilder<TaskActivity> b)
    {
        b.Property(a => a.Description).HasMaxLength(1000).IsRequired();
        b.HasIndex(a => new { a.TaskId, a.OccurredAt });
    }

    public void Configure(EntityTypeBuilder<TaskComment> b)
    {
        b.Property(c => c.Body).HasMaxLength(4000).IsRequired();
        b.HasIndex(c => new { c.TaskId, c.CreatedAt });
    }

    public void Configure(EntityTypeBuilder<QCReview> b)
    {
        b.Property(q => q.Comments).HasMaxLength(4000);
        b.Property(q => q.Environment).HasMaxLength(200);
        b.Property(q => q.BuildVersion).HasMaxLength(100);
        b.HasOne(q => q.Task).WithMany(t => t.QCReviews).HasForeignKey(q => q.TaskId);

        // Every QC attempt is retained; the pair is unique so an attempt number is never reused.
        b.HasIndex(q => new { q.TaskId, q.AttemptNumber }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(n => n.Title).HasMaxLength(300).IsRequired();
        b.Property(n => n.Body).HasMaxLength(2000);
        b.Property(n => n.LinkEntityType).HasMaxLength(50);

        // The bell-icon query: unread first, newest first, for one user.
        b.HasIndex(n => new { n.RecipientUserId, n.IsRead, n.CreatedAt });
    }
}

public class RequestSupportingConfig :
    IEntityTypeConfiguration<RequestClarification>, IEntityTypeConfiguration<TaskCollaborator>
{
    public void Configure(EntityTypeBuilder<RequestClarification> b)
    {
        b.Property(c => c.Question).HasMaxLength(2000).IsRequired();
        b.Property(c => c.Answer).HasMaxLength(2000);
        b.HasOne(c => c.Request).WithMany(r => r.Clarifications).HasForeignKey(c => c.RequestId);
        b.HasIndex(c => new { c.RequestId, c.AskedAt });
    }

    public void Configure(EntityTypeBuilder<TaskCollaborator> b)
    {
        b.HasKey(x => new { x.TaskId, x.UserId });
        b.HasOne(x => x.Task).WithMany(t => t.Collaborators).HasForeignKey(x => x.TaskId);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
