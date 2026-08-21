using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Services;
using WorkflowApp.Application.Requests.Services;
using WorkflowApp.Application.Tasks.Services;
using WorkflowApp.Application.Workforce.Services;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Workforce;
using WorkflowApp.Domain.Enums;
using WorkflowApp.Infrastructure.Identity;
using WorkflowApp.Infrastructure.Persistence;

namespace WorkflowApp.Application.Tests;

/// <summary>A clock the test controls, so lockout and expiry windows can be crossed deliberately.</summary>
public sealed class FixedClock : IDateTimeProvider
{
    public FixedClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
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

        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            // A unique name per harness keeps parallel test classes isolated.
            .UseInMemoryDatabase($"workflow-tests-{Guid.NewGuid():N}")
            .Options;

        Db = new WorkflowDbContext(options);

        CurrentUser = new TestCurrentUser();
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

        Shifts = new ShiftService(
            Db, PermissionService, Activity, Audit, CurrentUser, Clock, NullLogger<ShiftService>.Instance);

        WorkforceQueries = new WorkforceQueryService(Db, Calendar, Clock);

        ShiftMaintenance = new ShiftMaintenanceService(
            Db, Activity, Audit, Clock, Options.Create(WorkforceOptions),
            NullLogger<ShiftMaintenanceService>.Instance);

        Numbers = new NumberGenerator(Db);
        Requests = new RequestService(Db, Numbers, Clock);
        TaskCreation = new TaskCreationService(Db, Numbers, Clock);
        TaskQueries = new TaskQueryService(Db, CurrentUser);

        Triage = new RequestTriageService(
            Db, Requests, TaskCreation, Audit, Clock, NullLogger<RequestTriageService>.Instance);

        TaskWorkflow = new TaskWorkflowService(
            Db, CurrentUser, Audit, Activity, Clock, TaskQueries, NullLogger<TaskWorkflowService>.Instance);

        Assignment = new TaskAssignmentService(
            Db, TaskQueries, Clock, NullLogger<TaskAssignmentService>.Instance);

        WorkSessions = new WorkSessionService(
            Db, TaskQueries, Activity, Clock, NullLogger<WorkSessionService>.Instance);
    }

    public FixedClock Clock { get; }
    public WorkflowDbContext Db { get; }
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
    public IShiftService Shifts { get; }
    public IWorkforceQueryService WorkforceQueries { get; }
    public IShiftMaintenanceService ShiftMaintenance { get; }
    public INumberGenerator Numbers { get; }
    public IRequestService Requests { get; }
    public IRequestTriageService Triage { get; }
    public ITaskCreationService TaskCreation { get; }
    public ITaskQueryService TaskQueries { get; }
    public ITaskWorkflowService TaskWorkflow { get; }
    public ITaskAssignmentService Assignment { get; }
    public IWorkSessionService WorkSessions { get; }

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

    public void Dispose() => Db.Dispose();
}
