using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Events;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Services;
using WorkflowApp.Application.Notifications;
using WorkflowApp.Application.Reporting;
using WorkflowApp.Application.Admin.Services;
using WorkflowApp.Application.Requests.Services;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Application.Verifications.Services;
using WorkflowApp.Application.Workforce.Services;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Workforce;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Infrastructure.Identity;
using WorkflowApp.Infrastructure.Persistence;
using WorkflowApp.Infrastructure.Persistence.Interceptors;
using WorkflowApp.Infrastructure.Storage;

namespace WorkflowApp.Application.Tests;

/// <summary>A clock the test controls, so lockout and expiry windows can be crossed deliberately.</summary>
public sealed class FixedClock : IDateTimeProvider
{
    public FixedClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>Captures what would have gone out over SignalR, so routing-free tests can assert on it.</summary>
public sealed class RecordingEventPublisher : IIntegrationEventPublisher
{
    public List<IntegrationEvent> Published { get; } = new();

    public Task PublishAsync(IReadOnlyList<IntegrationEvent> events, CancellationToken ct = default)
    {
        Published.AddRange(events);
        return Task.CompletedTask;
    }
}

public sealed class TestCurrentUser : ICurrentUser
{
    public long? UserId { get; set; }
    public string? UserName { get; set; }
    public bool IsAuthenticated => UserId.HasValue;
    public IReadOnlySet<string> Permissions { get; set; } = new HashSet<string>();
    public string? IpAddress { get; set; } = "127.0.0.1";
    public string? UserAgent { get; set; } = "xunit";
}

/// <summary>
/// Wires the real services against an in-memory database. Deliberately uses the production
/// <see cref="WorkflowDbContext"/> and the production password hasher and token service, so these
/// tests exercise the actual model and the actual crypto — only the storage engine is substituted.
/// </summary>
public sealed class TestHarness : IDisposable
{
    public const string TestSigningKey = "test-only-signing-key-at-least-32-bytes-long!!";

    public TestHarness(DateTimeOffset? now = null, string? timeZoneId = null)
    {
        Clock = new FixedClock(now ?? new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

        EventQueue = new IntegrationEventQueue();
        Events = new RecordingEventPublisher();
        CurrentUser = new TestCurrentUser();

        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            // A unique name per harness keeps parallel test classes isolated.
            .UseInMemoryDatabase($"workflow-tests-{Guid.NewGuid():N}")
            // The production interceptors. The auditing one matters beyond convenience: without it
            // CreatedAt would come from the wall clock while everything else came from FixedClock,
            // and any test that measures an elapsed time would be comparing two different clocks.
            .AddInterceptors(
                new AuditableEntityInterceptor(CurrentUser, Clock),
                new IntegrationEventDispatchInterceptor(
                    EventQueue, Events, NullLogger<IntegrationEventDispatchInterceptor>.Instance))
            .Options;

        Db = new WorkflowDbContext(options);

        PasswordHasher = new PasswordHasherAdapter();

        AuthOptions = new AuthOptions
        {
            MaxFailedLoginAttempts = 3,
            LockoutMinutes = 15,
            MinimumPasswordLength = 10
        };

        TokenService = new JwtTokenService(
            Options.Create(new JwtOptions
            {
                Issuer = "WorkflowApp.Tests",
                Audience = "WorkflowApp.Tests",
                AccessTokenMinutes = 30,
                RefreshTokenDays = 14,
                SigningKey = TestSigningKey
            }),
            Clock);

        Audit = new AuditService(Db, CurrentUser);
        Activity = new ActivityLogger(Db, Clock);
        PermissionService = new PermissionService(Db);

        WorkforceOptions = new WorkforceOptions
        {
            TimeZoneId = timeZoneId ?? "UTC",
            MaxShiftHours = 16,
            AutoCloseStaleShifts = true
        };

        Calendar = new BusinessCalendar(
            Options.Create(WorkforceOptions), NullLogger<BusinessCalendar>.Instance);

        Auth = new AuthService(
            Db, PasswordHasher, TokenService, PermissionService, Audit, Activity, CurrentUser, Clock,
            Options.Create(AuthOptions), NullLogger<AuthService>.Instance);

        UserAdmin = new UserAdminService(
            Db, PasswordHasher, PermissionService, Audit, Clock, Options.Create(AuthOptions));

        Setup = new SetupService(Db, Audit);

        Shifts = new ShiftService(
            Db, PermissionService, Activity, Audit, CurrentUser, Clock, NullLogger<ShiftService>.Instance);

        WorkforceQueries = new WorkforceQueryService(Db, Calendar, Clock);

        ShiftMaintenance = new ShiftMaintenanceService(
            Db, Activity, Audit, Clock, Options.Create(WorkforceOptions),
            NullLogger<ShiftMaintenanceService>.Instance);

        Notifications = new NotificationService(Db, Clock);
        Dashboards = new DashboardService(Db, Calendar, Clock);
        Reports = new ReportService(Db, Calendar, Clock);
        AuditQueries = new AuditQueryService(Db);
        Numbers = new NumberGenerator(Db);
        Lookups = new LookupService(Db);
        Verifications = new VerificationService(
            Db, Numbers, Audit, Notifications, Clock, NullLogger<VerificationService>.Instance);

        Requests = new RequestService(Db, Numbers, Notifications, Lookups, Clock, Calendar, Verifications);
        TaskCreation = new TaskCreationService(Db, Numbers, Clock);
        Dependencies = new TaskDependencyService(Db, Clock);
        TaskQueries = new TaskQueryService(Db, CurrentUser, Dependencies, Calendar, Clock);

        Triage = new RequestTriageService(
            Db, Requests, TaskCreation, Verifications, Audit, Notifications, Lookups, Clock,
            NullLogger<RequestTriageService>.Instance);

        TaskWorkflow = new TaskWorkflowService(
            Db, CurrentUser, Audit, Activity, Clock, TaskQueries, NullLogger<TaskWorkflowService>.Instance);

        Assignment = new TaskAssignmentService(
            Db, TaskQueries, Notifications, EventQueue, Clock,
            NullLogger<TaskAssignmentService>.Instance);

        WorkSessions = new WorkSessionService(
            Db, TaskQueries, Dependencies, Activity, Notifications, Clock,
            NullLogger<WorkSessionService>.Instance);

        // Real storage under a throwaway directory, not a stub: the attachment path writes files,
        // hashes them and enforces the extension allow-list, and a fake would test none of that.
        // Disposed with the harness.
        FileStorageRoot = Path.Combine(Path.GetTempPath(), "wfa-tests", Guid.NewGuid().ToString("N"));

        Storage = new DiskFileStorage(
            Options.Create(new FileStorageOptions { Root = FileStorageRoot }),
            NullLogger<DiskFileStorage>.Instance);

        Attachments = new AttachmentService(Db, Storage, Audit, CurrentUser);

        QC = new QCService(
            Db, Attachments, TaskQueries, Activity, Audit, Notifications, Clock, NullLogger<QCService>.Instance);

        Closure = new ClosureService(
            Db, TaskQueries, Activity, Audit, Notifications, Clock, NullLogger<ClosureService>.Instance);

        Batches = new RequestBatchService(
            Db, Numbers, Notifications, Lookups, TaskCreation, Audit, Clock,
            NullLogger<RequestBatchService>.Instance);

        QuickWork = new QuickWorkService(
            Db, WorkSessions, Requests, Lookups, Activity, Calendar, Clock,
            NullLogger<QuickWorkService>.Instance);

        Comments = new TaskCommentService(Db, CurrentUser, Clock);
        ScopeChanges = new ScopeChangeService(Db, Audit, Clock);
    }

    public FixedClock Clock { get; }
    public WorkflowDbContext Db { get; }
    public IIntegrationEventQueue EventQueue { get; }
    public RecordingEventPublisher Events { get; }
    public TestCurrentUser CurrentUser { get; }
    public IPasswordHasher PasswordHasher { get; }
    public ITokenService TokenService { get; }
    public IAuditService Audit { get; }
    public IPermissionService PermissionService { get; }
    public IActivityLogger Activity { get; }
    public IBusinessCalendar Calendar { get; }
    public AuthOptions AuthOptions { get; }
    public WorkforceOptions WorkforceOptions { get; }
    public IAuthService Auth { get; }
    public IUserAdminService UserAdmin { get; }
    public ISetupService Setup { get; }
    public IShiftService Shifts { get; }
    public IWorkforceQueryService WorkforceQueries { get; }
    public IShiftMaintenanceService ShiftMaintenance { get; }
    public INumberGenerator Numbers { get; }
    public IRequestService Requests { get; }
    public IRequestTriageService Triage { get; }
    public IVerificationService Verifications { get; }
    public ITaskCreationService TaskCreation { get; }
    public ITaskQueryService TaskQueries { get; }
    public ITaskWorkflowService TaskWorkflow { get; }
    public ITaskAssignmentService Assignment { get; }
    public IWorkSessionService WorkSessions { get; }
    public IQuickWorkService QuickWork { get; }
    public IRequestBatchService Batches { get; }
    public IQCService QC { get; }
    public IAttachmentService Attachments { get; }
    public IFileStorage Storage { get; }

    /// <summary>Throwaway directory the attachment tests write into. Removed on dispose.</summary>
    public string FileStorageRoot { get; }
    public IClosureService Closure { get; }
    public ITaskDependencyService Dependencies { get; }
    public ILookupService Lookups { get; }
    public INotificationService Notifications { get; }
    public IDashboardService Dashboards { get; }
    public IReportService Reports { get; }
    public IAuditQueryService AuditQueries { get; }
    public ITaskCommentService Comments { get; }
    public IScopeChangeService ScopeChanges { get; }

    /// <summary>
    /// Sets the ambient caller. Services read permissions from <see cref="ICurrentUser"/>, so a test
    /// that exercises a permission-gated path has to say who is acting.
    /// </summary>
    public TestHarness ActingAs(long userId, params string[] permissions)
    {
        CurrentUser.UserId = userId;
        CurrentUser.Permissions = permissions.ToHashSet(StringComparer.Ordinal);
        return this;
    }

    /// <summary>Everything a fully-privileged actor can do — for tests not about authorization.</summary>
    public TestHarness ActingAsAdmin(long userId) => ActingAs(userId, Permissions.All);

    /// <summary>
    /// Puts a user on shift directly, bypassing <see cref="IShiftService"/>. Used by tests whose
    /// subject is something else (a work session, a timeline) and that only need the precondition.
    /// </summary>
    public async Task<ShiftSession> StartShiftAsync(long userId)
    {
        var user = await Db.Users.FirstAsync(u => u.Id == userId);
        user.WorkforceState = WorkforceState.Available;

        var shift = new ShiftSession { UserId = userId, ShiftStart = Clock.UtcNow };
        Db.ShiftSessions.Add(shift);
        await Db.SaveChangesAsync();
        return shift;
    }

    /// <summary>Creates the permission rows and the default roles with their grants.</summary>
    public async Task<TestHarness> SeedRolesAndPermissionsAsync()
    {
        var permissions = Permissions.All
            .Select(key => new Permission { Key = key })
            .ToList();

        Db.Permissions.AddRange(permissions);
        await Db.SaveChangesAsync();

        var byKey = permissions.ToDictionary(p => p.Key, p => p.Id);

        foreach (var (roleName, keys) in DefaultRoles.Map)
        {
            var role = new Role { Name = roleName, IsSystemRole = true };
            Db.Roles.Add(role);
            await Db.SaveChangesAsync();

            Db.RolePermissions.AddRange(keys.Select(k => new RolePermission
            {
                RoleId = role.Id,
                PermissionId = byKey[k]
            }));
        }

        // The same pause vocabulary the real seeder installs. Kept here because the two axes a
        // reason carries — does the TASK stop, and did the PERSON go somewhere — are what several
        // tests are actually about.
        Db.PauseReasons.AddRange(
            new PauseReason
            {
                Name = "Break", Category = PauseCategory.Break,
                IsBlocker = false, AwayState = WorkforceState.Break
            },
            new PauseReason
            {
                Name = "Lunch", Category = PauseCategory.Lunch,
                IsBlocker = false, AwayState = WorkforceState.Lunch
            },
            new PauseReason
            {
                Name = "Meeting", Category = PauseCategory.Meeting,
                IsBlocker = false, AwayState = WorkforceState.Meeting
            },
            new PauseReason
            {
                Name = "End of shift", Category = PauseCategory.EndOfShift, IsBlocker = false
            },
            new PauseReason
            {
                Name = "Other work became urgent", Category = PauseCategory.OtherWorkUrgent,
                RequiresComment = true, IsBlocker = false
            },
            new PauseReason
            {
                Name = "Waiting for client", Category = PauseCategory.WaitingForClient,
                RequiresComment = true, IsBlocker = true
            },
            new PauseReason
            {
                Name = "Waiting for someone", Category = PauseCategory.WaitingForSomeone,
                RequiresComment = true, IsBlocker = true
            },
            new PauseReason
            {
                Name = "Cannot continue — problem", Category = PauseCategory.CannotContinue,
                RequiresComment = true, IsBlocker = true
            },
            new PauseReason
            {
                Name = "Something else", Category = PauseCategory.Other,
                RequiresComment = true, IsBlocker = false
            });

        await Db.SaveChangesAsync();
        return this;
    }

    /// <summary>Creates an active user with a known password and optional roles.</summary>
    public async Task<User> CreateUserAsync(
        string userName = "worker1",
        string password = "CorrectHorse1",
        bool isActive = true,
        params string[] roles)
    {
        var user = new User
        {
            UserName = userName,
            Email = $"{userName}@workflowapp.local",
            DisplayName = userName,
            PasswordHash = PasswordHasher.Hash(password),
            IsActive = isActive
        };

        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        foreach (var roleName in roles)
        {
            var role = await Db.Roles.FirstAsync(r => r.Name == roleName);
            Db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        await Db.SaveChangesAsync();
        return user;
    }

    public void Dispose()
    {
        Db.Dispose();

        // The files a test wrote go with it. Best effort: a locked handle on a temp file is not
        // worth failing an otherwise green test run over.
        try
        {
            if (Directory.Exists(FileStorageRoot)) Directory.Delete(FileStorageRoot, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
