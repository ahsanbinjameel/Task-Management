using Microsoft.EntityFrameworkCore;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Entities.Workforce;

namespace WorkflowApp.Infrastructure.Persistence;

public class WorkflowDbContext : DbContext
{
    public WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : base(options) { }

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
    public DbSet<RequestClarification> RequestClarifications => Set<RequestClarification>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    // Tasks
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<TaskCollaborator> TaskCollaborators => Set<TaskCollaborator>();
    public DbSet<WorkSession> WorkSessions => Set<WorkSession>();
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
    }
}
