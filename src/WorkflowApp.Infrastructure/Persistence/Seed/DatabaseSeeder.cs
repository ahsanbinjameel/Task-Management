using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Infrastructure.Persistence.Seed;

/// <summary>
/// Brings the database up to the baseline every environment needs: the permission catalog, the
/// system roles and their grants, default pause reasons, and a bootstrap administrator.
///
/// Idempotent — safe to run on every startup. It only ever adds what is missing; it never deletes,
/// so hand-made roles and custom grants survive.
/// </summary>
public sealed class DatabaseSeeder
{
    private readonly WorkflowDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AuthOptions _options;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        WorkflowDbContext db,
        IPasswordHasher passwordHasher,
        IOptions<AuthOptions> options,
        ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedPermissionsAsync(ct);
        await SeedRolesAsync(ct);
        await SeedPauseReasonsAsync(ct);
        await SeedAdministratorAsync(ct);
    }

    /// <summary>Adds any permission key in the catalog that is not yet a row.</summary>
    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.Select(p => p.Key).ToListAsync(ct);
        var missing = Permissions.All.Except(existing, StringComparer.Ordinal).ToList();

        if (missing.Count == 0) return;

        _db.Permissions.AddRange(missing.Select(key => new Permission
        {
            Key = key,
            Description = DescribePermission(key)
        }));

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} permission(s): {Keys}", missing.Count, string.Join(", ", missing));
    }

    /// <summary>
    /// Creates each default role and reconciles its grants. Grants are additive: a permission
    /// added to the catalog later flows into the role on the next startup, but a grant an admin
    /// added by hand is never stripped.
    /// </summary>
    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var permissionIdsByKey = await _db.Permissions.ToDictionaryAsync(p => p.Key, p => p.Id, ct);

        foreach (var (roleName, permissionKeys) in DefaultRoles.Map)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName, ct);

            if (role is null)
            {
                role = new Role
                {
                    Name = roleName,
                    Description = DescribeRole(roleName),
                    IsSystemRole = true
                };
                _db.Roles.Add(role);
                await _db.SaveChangesAsync(ct);   // need the Id to link grants
                _logger.LogInformation("Seeded role {Role}", roleName);
            }

            var alreadyGranted = await _db.RolePermissions
                .Where(rp => rp.RoleId == role.Id)
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);

            var toGrant = permissionKeys
                .Where(permissionIdsByKey.ContainsKey)
                .Select(key => permissionIdsByKey[key])
                .Except(alreadyGranted)
                .ToList();

            if (toGrant.Count == 0) continue;

            _db.RolePermissions.AddRange(toGrant.Select(id => new RolePermission
            {
                RoleId = role.Id,
                PermissionId = id
            }));

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Granted {Count} permission(s) to role {Role}", toGrant.Count, roleName);
        }
    }

    private async Task SeedPauseReasonsAsync(CancellationToken ct)
    {
        if (await _db.PauseReasons.AnyAsync(ct)) return;

        // IsBlocker distinguishes "I stepped away" (Paused) from "I cannot proceed" (Blocked) —
        // the two land in different workflow states and read differently on a dashboard.
        _db.PauseReasons.AddRange(
            new PauseReason { Name = "Break", RequiresComment = false, IsBlocker = false },
            new PauseReason { Name = "Lunch", RequiresComment = false, IsBlocker = false },
            new PauseReason { Name = "Meeting", RequiresComment = false, IsBlocker = false },
            new PauseReason { Name = "End of shift", RequiresComment = false, IsBlocker = false },
            new PauseReason { Name = "Switched to higher priority task", RequiresComment = true, IsBlocker = false },
            new PauseReason { Name = "Waiting for client response", RequiresComment = true, IsBlocker = true },
            new PauseReason { Name = "Waiting for another team", RequiresComment = true, IsBlocker = true },
            new PauseReason { Name = "Blocked by dependency", RequiresComment = true, IsBlocker = true },
            new PauseReason { Name = "Environment or access issue", RequiresComment = true, IsBlocker = true },
            new PauseReason { Name = "Awaiting clarification", RequiresComment = true, IsBlocker = true });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded default pause reasons");
    }

    /// <summary>
    /// Creates a bootstrap administrator only when the database has no users at all. Once anyone
    /// exists this is a no-op, so the seeded password can never resurrect or overwrite an account.
    /// </summary>
    private async Task SeedAdministratorAsync(CancellationToken ct)
    {
        if (await _db.Users.AnyAsync(ct)) return;

        var adminRole = await _db.Roles.FirstAsync(r => r.Name == DefaultRoles.Administrator, ct);

        var admin = new User
        {
            UserName = _options.DefaultAdminUserName,
            Email = _options.DefaultAdminEmail,
            DisplayName = _options.DefaultAdminDisplayName,
            PasswordHash = _passwordHasher.Hash(_options.DefaultAdminPassword),
            IsActive = true,
            WorkforceState = WorkforceState.NotLoggedIn
        };

        _db.Users.Add(admin);
        await _db.SaveChangesAsync(ct);

        _db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Seeded bootstrap administrator '{UserName}'. Change this password immediately.",
            admin.UserName);
    }

    private static string DescribeRole(string roleName) => roleName switch
    {
        DefaultRoles.Administrator => "Full system access, including user, role and configuration management.",
        DefaultRoles.Requester => "Submits requests and tracks their own submissions.",
        DefaultRoles.Reviewer => "Triages incoming requests: approve, reject, clarify, duplicate, defer.",
        DefaultRoles.AssignmentManager => "Assigns approved tasks and manages workload distribution.",
        DefaultRoles.Worker => "Executes assigned tasks and records work sessions.",
        DefaultRoles.QC => "Performs quality control review and closes passed tasks.",
        DefaultRoles.Management => "Read-only oversight of pipeline, workforce and reports.",
        _ => string.Empty
    };

    private static string DescribePermission(string key) => key switch
    {
        Permissions.RequestCreate => "Submit a new request.",
        Permissions.RequestViewOwn => "View requests the user submitted.",
        Permissions.RequestViewAll => "View every request in the system.",
        Permissions.TaskReview => "Triage a request: start review, request clarification, mark duplicate.",
        Permissions.TaskApprove => "Approve a request so it becomes an executable task.",
        Permissions.TaskAssign => "Assign or reassign tasks and manage the queue.",
        Permissions.TaskWork => "Start, pause, block and complete assigned work.",
        Permissions.TaskQCReview => "Perform QC review and record pass/fail outcomes.",
        Permissions.TaskClose => "Close a task that has passed QC.",
        Permissions.TaskReopen => "Reopen a closed task, with a mandatory reason.",
        Permissions.TaskCancel => "Cancel a task before completion, with a mandatory reason.",
        Permissions.TaskDefer => "Defer an approved task to a later date.",
        Permissions.TaskOverride => "Force a status transition outside the workflow map. Always audited.",
        Permissions.WorkforceViewAll => "View shift status and availability of all employees.",
        Permissions.WorkforceManageOthers => "Close or correct another employee's shift.",
        Permissions.WorkforceTrackShift => "Start and end shifts and set availability. For people who execute tasks.",
        Permissions.DashboardManagement => "Access the management dashboard.",
        Permissions.ReportsView => "View and export reports.",
        Permissions.AdminManageUsers => "Create, edit, activate and deactivate user accounts.",
        Permissions.AdminManageRoles => "Manage roles and their permission grants.",
        Permissions.AdminManageConfig => "Manage system configuration and lookup data.",
        Permissions.AdminViewAudit => "Read the security audit log.",
        _ => string.Empty
    };
}
