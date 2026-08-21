using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;

namespace WorkflowApp.Application.Identity.Services;

/// <summary>
/// Resolves a user's effective permissions — the union of every permission granted by every role
/// they hold. There is no direct user→permission grant by design: roles are the only bundle.
/// </summary>
public interface IPermissionService
{
    Task<IReadOnlyList<string>> GetPermissionsAsync(long userId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRolesAsync(long userId, CancellationToken ct = default);
    Task<bool> HasPermissionAsync(long userId, string permissionKey, CancellationToken ct = default);
}

public sealed class PermissionService : IPermissionService
{
    private readonly IWorkflowDbContext _db;

    public PermissionService(IWorkflowDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(long userId, CancellationToken ct = default)
    {
        // Join through UserRole → RolePermission → Permission. Distinct because two roles may
        // grant the same permission.
        var keys = await (
            from ur in _db.UserRoles.AsNoTracking()
            where ur.UserId == userId
            join rp in _db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
            join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            select p.Key)
            .Distinct()
            .ToListAsync(ct);

        return keys;
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(long userId, CancellationToken ct = default)
    {
        var names = await (
            from ur in _db.UserRoles.AsNoTracking()
            where ur.UserId == userId
            join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            select r.Name)
            .Distinct()
            .ToListAsync(ct);

        return names;
    }

    public async Task<bool> HasPermissionAsync(long userId, string permissionKey, CancellationToken ct = default)
    {
        var permissions = await GetPermissionsAsync(userId, ct);
        return permissions.Contains(permissionKey, StringComparer.Ordinal);
    }
}
