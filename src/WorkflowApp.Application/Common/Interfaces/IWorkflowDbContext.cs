using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WorkflowApp.Domain.Entities.Common;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Entities.Verifications;
using WorkflowApp.Domain.Entities.Workforce;

namespace WorkflowApp.Application.Common.Interfaces;

/// <summary>
/// The persistence surface the Application layer is allowed to see. Infrastructure's
/// <c>WorkflowDbContext</c> implements it. Keeping this as an interface lets use-case services
/// be exercised against the EF Core InMemory provider without a SQL Server instance.
/// </summary>
public interface IWorkflowDbContext
{
    // Shared
    DbSet<NumberSequence> NumberSequences { get; }

    // Identity
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<LoginAttempt> LoginAttempts { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    // Workforce
    DbSet<ShiftSession> ShiftSessions { get; }
    DbSet<ActivityEvent> ActivityEvents { get; }

    // Organization
    DbSet<Department> Departments { get; }
    DbSet<Team> Teams { get; }
    DbSet<Client> Clients { get; }
    DbSet<Project> Projects { get; }
    DbSet<Module> Modules { get; }
    DbSet<Form> Forms { get; }
    DbSet<FormSurface> FormSurfaces { get; }
    DbSet<PauseReason> PauseReasons { get; }

    // Requests
    DbSet<Request> Requests { get; }

    /// <summary>Several things asked for at once. See <see cref="RequestBatch"/>.</summary>
    DbSet<RequestBatch> RequestBatches { get; }
    DbSet<RequestClarification> RequestClarifications { get; }
    DbSet<RequestActivity> RequestActivities { get; }
    DbSet<Attachment> Attachments { get; }

    // Verifications — assigned investigation, no completed task required. See Verification.
    DbSet<Verification> Verifications { get; }
    DbSet<VerificationActivity> VerificationActivities { get; }

    // Tasks
    DbSet<WorkTask> Tasks { get; }
    DbSet<TaskCollaborator> TaskCollaborators { get; }
    DbSet<WorkSession> WorkSessions { get; }

    /// <summary>Interruptions that never became requests. See <see cref="QuickWork"/>.</summary>
    DbSet<QuickWork> QuickWork { get; }
    DbSet<QCReview> QCReviews { get; }
    DbSet<AssignmentHistory> AssignmentHistories { get; }
    DbSet<StatusHistory> StatusHistories { get; }
    DbSet<TaskActivity> TaskActivities { get; }
    DbSet<TaskComment> TaskComments { get; }
    DbSet<TaskDependency> TaskDependencies { get; }
    DbSet<ScopeChange> ScopeChanges { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<AuditLog> AuditLogs { get; }

    /// <summary>Transactions live here; multi-step operations must commit atomically.</summary>
    DatabaseFacade Database { get; }

    /// <summary>Needed to reload or detach entries after a concurrency conflict.</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
