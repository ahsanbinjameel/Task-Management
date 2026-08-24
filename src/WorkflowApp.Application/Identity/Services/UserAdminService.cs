using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Application.Identity.Dtos;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Identity.Services;

/// <summary>
/// Administrative user management. Every method here is gated by <c>Admin.ManageUsers</c> or
/// <c>Admin.ManageRoles</c> at the API boundary; the service assumes authorization already passed
/// and concentrates on invariants and audit.
/// </summary>
public interface IUserAdminService
{
    Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<Result<UserDto>> GetUserAsync(long userId, CancellationToken ct = default);
    Task<PagedResult<UserDto>> ListUsersAsync(PageQuery page, string? search = null, bool? isActive = null, CancellationToken ct = default);
    Task<Result> SetActiveAsync(long userId, bool isActive, CancellationToken ct = default);
    Task<Result<UserDto>> AssignRolesAsync(long userId, IReadOnlyList<string> roleNames, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(long userId, string newPassword, CancellationToken ct = default);
    Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default);
}

public sealed class UserAdminService : IUserAdminService
{
    private readonly IWorkflowDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;
    private readonly AuthOptions _authOptions;

    public UserAdminService(
        IWorkflowDbContext db,
        IPasswordHasher passwordHasher,
        IPermissionService permissions,
        IAuditService audit,
        IDateTimeProvider clock,
        IOptions<AuthOptions> authOptions)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _permissions = permissions;
        _audit = audit;
        _clock = clock;
        _authOptions = authOptions.Value;
    }

    public async Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var userName = request.UserName.Trim();
        // Empty and whitespace both mean "no address"; normalise to null so the filtered unique
        // index sees them all the same way.
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        if (PasswordPolicy.Validate(request.Password, _authOptions.MinimumPasswordLength) is { } policyError)
            return Result<UserDto>.Failure(policyError);

        // Unique indexes back this up at the DB level; checking here gives a usable error message.
        if (await _db.Users.AnyAsync(u => u.UserName == userName, ct))
            return Result<UserDto>.Failure(Error.Conflict("user.username_taken", "That username is already in use."));

        if (email is not null && await _db.Users.AnyAsync(u => u.Email == email, ct))
            return Result<UserDto>.Failure(Error.Conflict("user.email_taken", "That email address is already in use."));

        var roles = await ResolveRolesAsync(request.Roles, ct);
        if (roles.IsFailure)
            return Result<UserDto>.Failure(roles.Error!);

        var user = new User
        {
            UserName = userName,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            IsActive = true,
            DepartmentId = request.DepartmentId,
            TeamId = request.TeamId,
            WorkforceState = WorkforceState.NotLoggedIn
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);   // need the generated Id before linking roles

        foreach (var role in roles.Value!)
            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

        _audit.Record(
            AuditActions.UserCreated,
            entityType: nameof(User),
            entityId: user.Id,
            newValues: new { user.UserName, user.Email, user.DisplayName, Roles = roles.Value!.Select(r => r.Name) });

        await _db.SaveChangesAsync(ct);

        var roleNames = roles.Value!.Select(r => r.Name).ToList();
        var permissionKeys = await _permissions.GetPermissionsAsync(user.Id, ct);
        return Result<UserDto>.Success(UserMapper.ToDto(user, roleNames, permissionKeys));
    }

    public async Task<Result<UserDto>> GetUserAsync(long userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<UserDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        var roles = await _permissions.GetRolesAsync(userId, ct);
        var permissions = await _permissions.GetPermissionsAsync(userId, ct);
        return Result<UserDto>.Success(UserMapper.ToDto(user, roles, permissions));
    }

    public async Task<PagedResult<UserDto>> ListUsersAsync(
        PageQuery page, string? search = null, bool? isActive = null, CancellationToken ct = default)
    {
        var query = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u =>
                u.UserName.Contains(term) ||
                (u.Email != null && u.Email.Contains(term)) ||
                u.DisplayName.Contains(term));
        }

        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);

        var total = await query.CountAsync(ct);

        var users = await query
            .OrderBy(u => u.DisplayName)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync(ct);

        // One round trip for the role names of the whole page rather than one per user.
        var userIds = users.Select(u => u.Id).ToList();
        var rolesByUser = await (
            from ur in _db.UserRoles.AsNoTracking()
            where userIds.Contains(ur.UserId)
            join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            select new { ur.UserId, r.Name })
            .ToListAsync(ct);

        var lookup = rolesByUser
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name).ToList());

        var items = users
            .Select(u => UserMapper.ToDto(
                u,
                lookup.TryGetValue(u.Id, out var names) ? names : Array.Empty<string>(),
                // Permission lists are omitted from list views — they are only meaningful for a
                // single user and would multiply the query cost across the page.
                Array.Empty<string>()))
            .ToList();

        return new PagedResult<UserDto>(items, page.NormalizedPage, page.NormalizedPageSize, total);
    }

    public async Task<Result> SetActiveAsync(long userId, bool isActive, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result.Failure(Error.NotFound("user.not_found", "User not found."));

        if (user.IsActive == isActive)
            return Result.Success();

        user.IsActive = isActive;

        if (!isActive)
        {
            // Deactivation must take effect now, not when the access token happens to expire.
            var live = await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ToListAsync(ct);

            foreach (var token in live)
            {
                token.RevokedAt = now;
            }

            user.WorkforceState = WorkforceState.NotLoggedIn;
        }

        _audit.Record(
            isActive ? AuditActions.UserActivated : AuditActions.UserDeactivated,
            entityType: nameof(User),
            entityId: userId,
            previousValues: new { IsActive = !isActive },
            newValues: new { IsActive = isActive });

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<UserDto>> AssignRolesAsync(
        long userId, IReadOnlyList<string> roleNames, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<UserDto>.Failure(Error.NotFound("user.not_found", "User not found."));

        var resolved = await ResolveRolesAsync(roleNames, ct);
        if (resolved.IsFailure)
            return Result<UserDto>.Failure(resolved.Error!);

        var previous = await _permissions.GetRolesAsync(userId, ct);

        // Replace the whole set: the request describes the desired end state.
        var existing = await _db.UserRoles.Where(ur => ur.UserId == userId).ToListAsync(ct);
        _db.UserRoles.RemoveRange(existing);

        foreach (var role in resolved.Value!)
            _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });


        _audit.Record(
            AuditActions.UserRolesChanged,
            entityType: nameof(User),
            entityId: userId,
            previousValues: new { Roles = previous },
            newValues: new { Roles = resolved.Value!.Select(r => r.Name) });

        await _db.SaveChangesAsync(ct);

        var permissions = await _permissions.GetPermissionsAsync(userId, ct);
        return Result<UserDto>.Success(
            UserMapper.ToDto(user, resolved.Value!.Select(r => r.Name).ToList(), permissions));
    }

    public async Task<Result> ResetPasswordAsync(long userId, string newPassword, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result.Failure(Error.NotFound("user.not_found", "User not found."));

        if (PasswordPolicy.Validate(newPassword, _authOptions.MinimumPasswordLength) is { } policyError)
            return Result.Failure(policyError);

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;

        var live = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in live)
        {
            token.RevokedAt = now;
        }

        _audit.Record(
            AuditActions.PasswordResetByAdmin,
            entityType: nameof(User),
            entityId: userId);

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct = default)
    {
        var roles = await _db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);

        var permissionsByRole = await (
            from rp in _db.RolePermissions.AsNoTracking()
            join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            select new { rp.RoleId, p.Key })
            .ToListAsync(ct);

        var lookup = permissionsByRole
            .GroupBy(x => x.RoleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Key).OrderBy(k => k).ToList());

        return roles
            .Select(r => new RoleDto(
                r.Id,
                r.Name,
                r.Description,
                r.IsSystemRole,
                lookup.TryGetValue(r.Id, out var keys) ? keys : Array.Empty<string>()))
            .ToList();
    }

    /// <summary>Maps role names to entities, failing loudly if any name is unknown.</summary>
    private async Task<Result<List<Role>>> ResolveRolesAsync(IReadOnlyList<string> roleNames, CancellationToken ct)
    {
        var wanted = roleNames.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct().ToList();
        if (wanted.Count == 0)
            return Result<List<Role>>.Success(new List<Role>());

        var found = await _db.Roles.Where(r => wanted.Contains(r.Name)).ToListAsync(ct);

        var missing = wanted.Except(found.Select(r => r.Name)).ToList();
        if (missing.Count > 0)
            return Result<List<Role>>.Failure(
                Error.Validation("role.unknown", $"Unknown role(s): {string.Join(", ", missing)}."));

        return Result<List<Role>>.Success(found);
    }
}
