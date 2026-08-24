using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Entities.Workforce;

namespace WorkflowApp.Infrastructure.Persistence.Configurations;

/// <summary>Shared conventions: ROWVERSION concurrency token on every BaseEntity.</summary>
public static class BaseEntityConventions
{
    public static void ApplyRowVersion<T>(EntityTypeBuilder<T> b) where T : BaseEntity
    {
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

public class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasIndex(u => u.UserName).IsUnique();

        // Email is optional, so the uniqueness index has to be filtered: SQL Server treats NULLs as
        // equal for a unique index, which would let exactly one user have no address and reject
        // every one after that. The filter keeps "no email" unlimited while still refusing
        // duplicates among the users who do have one.
        b.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL")
            .HasDatabaseName("UX_User_Email");

        b.Property(u => u.UserName).HasMaxLength(100).IsRequired();
        b.Property(u => u.Email).HasMaxLength(256);
        b.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        BaseEntityConventions.ApplyRowVersion(b);
    }
}

public class RolePermConfig :
    IEntityTypeConfiguration<Role>, IEntityTypeConfiguration<Permission>,
    IEntityTypeConfiguration<UserRole>, IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.HasIndex(r => r.Name).IsUnique();
        b.Property(r => r.Name).HasMaxLength(100).IsRequired();
    }
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.HasIndex(p => p.Key).IsUnique();
        b.Property(p => p.Key).HasMaxLength(100).IsRequired();
    }
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.HasKey(x => new { x.UserId, x.RoleId });
        b.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId);
        b.HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId);
    }
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.HasKey(x => new { x.RoleId, x.PermissionId });
        b.HasOne(x => x.Role).WithMany(r => r.RolePermissions).HasForeignKey(x => x.RoleId);
        b.HasOne(x => x.Permission).WithMany(p => p.RolePermissions).HasForeignKey(x => x.PermissionId);
    }
}

public class RequestConfig : IEntityTypeConfiguration<Request>
{
    public void Configure(EntityTypeBuilder<Request> b)
    {
        b.HasIndex(r => r.RequestNumber).IsUnique();
        b.Property(r => r.RequestNumber).HasMaxLength(30).IsRequired();
        b.Property(r => r.Title).HasMaxLength(300).IsRequired();
        b.HasIndex(r => r.Status);
        b.HasOne(r => r.RequestedByUser).WithMany().HasForeignKey(r => r.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // "The items in this batch, in the order they were typed" is asked on every batch screen
        // and every fold-together decision.
        b.HasIndex(r => new { r.BatchId, r.OrdinalInBatch });

        BaseEntityConventions.ApplyRowVersion(b);
    }
}

public class RequestBatchConfig : IEntityTypeConfiguration<RequestBatch>
{
    public void Configure(EntityTypeBuilder<RequestBatch> b)
    {
        b.HasIndex(x => x.BatchNumber).IsUnique();
        b.Property(x => x.BatchNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Note).HasMaxLength(4000);

        b.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.SetNull);

        // Deleting a batch must never take its items with it: each one is a request in its own
        // right, and some of them may already have become work.
        b.HasMany(x => x.Items).WithOne(r => r.Batch!).HasForeignKey(r => r.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        // The FK is named explicitly. Left to convention, EF cannot tell that `Attachment.BatchId`
        // backs this navigation and silently adds a second `RequestBatchId` column beside it —
        // two columns holding one fact, which is the drift this codebase spends its comments
        // avoiding.
        b.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        BaseEntityConventions.ApplyRowVersion(b);
    }
}

public class WorkTaskConfig : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> b)
    {
        b.HasIndex(t => t.TaskNumber).IsUnique();
        b.Property(t => t.TaskNumber).HasMaxLength(30).IsRequired();
        b.Property(t => t.Title).HasMaxLength(300).IsRequired();

        // Common query paths.
        b.HasIndex(t => t.Status);
        b.HasIndex(t => new { t.PrimaryAssigneeUserId, t.Status });
        b.HasIndex(t => new { t.PrimaryAssigneeUserId, t.QueueOrder });

        b.HasOne(t => t.PrimaryAssigneeUser).WithMany().HasForeignKey(t => t.PrimaryAssigneeUserId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(t => t.ParentTask).WithMany(t => t.SubTasks).HasForeignKey(t => t.ParentTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        // Existing subtasks predate the flag and used to block the parent unconditionally, so they
        // default to required — the migration must not quietly make finished work optional.
        b.Property(t => t.IsRequired).HasDefaultValue(true);

        // "Which of my subtasks are still outstanding" is asked on every parent task screen and on
        // every completion attempt.
        b.HasIndex(t => new { t.ParentTaskId, t.IsRequired, t.Status });

        BaseEntityConventions.ApplyRowVersion(b);
    }
}

public class WorkSessionConfig : IEntityTypeConfiguration<WorkSession>
{
    public void Configure(EntityTypeBuilder<WorkSession> b)
    {
        b.HasOne(s => s.Task).WithMany(t => t.WorkSessions).HasForeignKey(s => s.TaskId);
        b.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);

        // CRITICAL RULE: at most one Active (status = 0) work session per user.
        // Filtered unique index enforces "single active primary work session" at the DB level,
        // backing up the transactional check in the application service.
        b.HasIndex(s => s.UserId)
            .IsUnique()
            .HasFilter("[Status] = 0")
            .HasDatabaseName("UX_WorkSession_OneActivePerUser");

        BaseEntityConventions.ApplyRowVersion(b);
    }
}

public class QuickWorkConfig : IEntityTypeConfiguration<QuickWork>
{
    public void Configure(EntityTypeBuilder<QuickWork> b)
    {
        b.Property(q => q.Title).IsRequired().HasMaxLength(200);
        b.Property(q => q.Outcome).HasMaxLength(2000);

        b.HasOne(q => q.User).WithMany().HasForeignKey(q => q.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(q => q.Client).WithMany().HasForeignKey(q => q.ClientId).OnDelete(DeleteBehavior.SetNull);

        // Both point at records that outlive this one and must never take it with them: losing the
        // account of an interruption because the task it interrupted was cancelled would put a
        // hole in somebody's day that nothing else could explain.
        b.HasOne(q => q.InterruptedTask).WithMany()
            .HasForeignKey(q => q.InterruptedTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(q => q.PromotedToRequest).WithMany()
            .HasForeignKey(q => q.PromotedToRequestId).OnDelete(DeleteBehavior.Restrict);

        // The same rule the task timer keeps, for the same reason: one thing at a time, enforced
        // by the database and not only by the service that happens to be in front of it today.
        b.HasIndex(q => q.UserId)
            .IsUnique()
            .HasFilter("[Status] = 0")
            .HasDatabaseName("UX_QuickWork_OneActivePerUser");

        // The daily report asks "what did this person do on this day" every time it runs.
        b.HasIndex(q => new { q.UserId, q.StartedAt });

        BaseEntityConventions.ApplyRowVersion(b);
    }
}

public class ShiftSessionConfig : IEntityTypeConfiguration<ShiftSession>
{
    public void Configure(EntityTypeBuilder<ShiftSession> b)
    {
        b.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(s => new { s.UserId, s.ShiftStart });
        b.Property(s => s.EndNote).HasMaxLength(500);
        b.Property(s => s.StartDeviceInfo).HasMaxLength(512);
        b.Property(s => s.StartIpAddress).HasMaxLength(64);
        // The stale-shift sweep scans for open shifts across all users.
        b.HasIndex(s => s.ShiftEnd);
        // One open shift per user at a time (ShiftEnd IS NULL).
        b.HasIndex(s => s.UserId)
            .IsUnique()
            .HasFilter("[ShiftEnd] IS NULL")
            .HasDatabaseName("UX_ShiftSession_OneOpenPerUser");
        BaseEntityConventions.ApplyRowVersion(b);
    }
}

public class AttachmentConfig : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> b)
    {
        b.Property(a => a.OriginalFileName).HasMaxLength(400).IsRequired();
        b.Property(a => a.StoredPath).HasMaxLength(500).IsRequired();
        b.Property(a => a.ContentType).HasMaxLength(200).IsRequired();
        // Hex-encoded SHA-256 of the file — fixed 64 characters, used for duplicate detection.
        b.Property(a => a.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasIndex(a => a.RequestId);
        b.HasIndex(a => a.TaskId);
        b.HasIndex(a => a.BatchId);
        b.HasIndex(a => a.Sha256);

        // "The completion proof on this task", "this attempt's evidence" — both asked on every
        // task screen that shows either.
        b.HasIndex(a => new { a.TaskId, a.Kind });
        b.HasIndex(a => a.QCReviewId);

        // Evidence outlives nothing: a quality-check attempt is append-only history, so removing
        // one must never silently take the pictures that justified it.
        b.HasOne<QCReview>().WithMany()
            .HasForeignKey(a => a.QCReviewId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class DependencyConfig : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> b)
    {
        b.HasIndex(d => new { d.TaskId, d.RelatedTaskId, d.Type }).IsUnique();
    }
}

public class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(a => a.Action).HasMaxLength(100).IsRequired();
        b.HasIndex(a => new { a.EntityType, a.EntityId });
        b.HasIndex(a => a.CreatedAt);
    }
}
