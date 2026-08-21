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
        b.HasIndex(u => u.Email).IsUnique();
        b.Property(u => u.UserName).HasMaxLength(100).IsRequired();
        b.Property(u => u.Email).HasMaxLength(256).IsRequired();
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

public class ShiftSessionConfig : IEntityTypeConfiguration<ShiftSession>
{
    public void Configure(EntityTypeBuilder<ShiftSession> b)
    {
        b.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(s => new { s.UserId, s.ShiftStart });
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
        b.HasIndex(a => a.RequestId);
        b.HasIndex(a => a.TaskId);
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
