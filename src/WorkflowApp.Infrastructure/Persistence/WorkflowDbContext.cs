using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Entities.Common;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Entities.Verifications;
using WorkflowApp.Domain.Entities.Workforce;

namespace WorkflowApp.Infrastructure.Persistence;

public class WorkflowDbContext : DbContext, IWorkflowDbContext
{
    public WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : base(options) { }

    // Shared
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    // Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Workforce
    public DbSet<ShiftSession> ShiftSessions => Set<ShiftSession>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    // Organization
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<PauseReason> PauseReasons => Set<PauseReason>();

    // Requests
    public DbSet<Request> Requests => Set<Request>();
    public DbSet<RequestBatch> RequestBatches => Set<RequestBatch>();
    public DbSet<RequestClarification> RequestClarifications => Set<RequestClarification>();
    public DbSet<RequestActivity> RequestActivities => Set<RequestActivity>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    // Tasks
    // Verifications
    public DbSet<Verification> Verifications => Set<Verification>();
    public DbSet<VerificationActivity> VerificationActivities => Set<VerificationActivity>();

    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<TaskCollaborator> TaskCollaborators => Set<TaskCollaborator>();
    public DbSet<WorkSession> WorkSessions => Set<WorkSession>();
    public DbSet<QuickWork> QuickWork => Set<QuickWork>();
    public DbSet<QCReview> QCReviews => Set<QCReview>();
    public DbSet<AssignmentHistory> AssignmentHistories => Set<AssignmentHistory>();
    public DbSet<StatusHistory> StatusHistories => Set<StatusHistory>();
    public DbSet<TaskActivity> TaskActivities => Set<TaskActivity>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<ScopeChange> ScopeChanges => Set<ScopeChange>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Applies every IEntityTypeConfiguration in this assembly (keys, indexes, constraints).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkflowDbContext).Assembly);

        // ROWVERSION is a SQL Server type. On any other provider — the SQLite demo mode, or the
        // InMemory provider in tests — leave the column as an inert nullable blob rather than
        // letting EF try to treat it as a store-generated concurrency token it cannot produce.
        // SQL Server remains the deployment target; this only keeps the same model loadable
        // elsewhere for local runs.
        if (!Database.IsSqlServer())
        {
            foreach (var property in modelBuilder.Model.GetEntityTypes()
                         .SelectMany(e => e.GetProperties())
                         .Where(p => p.Name == nameof(BaseEntity.RowVersion)))
            {
                property.ValueGenerated = ValueGenerated.Never;
                property.IsConcurrencyToken = false;
            }
        }

        // SQLite has no native DateTimeOffset and refuses to ORDER BY one. Storing UTC ticks as an
        // integer keeps ordering and range comparisons exact — every timestamp in this application
        // is already UTC, so nothing is lost by dropping the offset on the way in.
        if (Database.IsSqlite())
        {
            var toTicks = new ValueConverter<DateTimeOffset, long>(
                value => value.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

            var toNullableTicks = new ValueConverter<DateTimeOffset?, long?>(
                value => value == null ? null : value.Value.UtcTicks,
                ticks => ticks == null ? null : new DateTimeOffset(ticks.Value, TimeSpan.Zero));

            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            {
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(toTicks);
                else if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(toNullableTicks);
            }
        }
    }
}
