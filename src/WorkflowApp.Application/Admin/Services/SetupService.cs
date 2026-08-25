using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Admin.Dtos;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Admin.Services;

public interface ISetupService
{
    Task<IReadOnlyList<ClientDto>> ClientsAsync(CancellationToken ct = default);
    Task<Result<ClientDto>> CreateClientAsync(SaveClientDto dto, CancellationToken ct = default);
    Task<Result<ClientDto>> UpdateClientAsync(long id, SaveClientDto dto, CancellationToken ct = default);
    Task<Result<ClientDto>> SetClientActiveAsync(long id, bool isActive, CancellationToken ct = default);

    Task<IReadOnlyList<DepartmentDto>> DepartmentsAsync(CancellationToken ct = default);
    Task<Result<DepartmentDto>> CreateDepartmentAsync(SaveDepartmentDto dto, CancellationToken ct = default);
    Task<Result<DepartmentDto>> UpdateDepartmentAsync(long id, SaveDepartmentDto dto, CancellationToken ct = default);
    Task<Result<DepartmentDto>> SetDepartmentActiveAsync(long id, bool isActive, CancellationToken ct = default);

    Task<IReadOnlyList<TeamDto>> TeamsAsync(CancellationToken ct = default);
    Task<Result<TeamDto>> CreateTeamAsync(SaveTeamDto dto, CancellationToken ct = default);
    Task<Result<TeamDto>> UpdateTeamAsync(long id, SaveTeamDto dto, CancellationToken ct = default);
    Task<Result<TeamDto>> SetTeamActiveAsync(long id, bool isActive, CancellationToken ct = default);

    Task<IReadOnlyList<PauseReasonDetailDto>> PauseReasonsAsync(CancellationToken ct = default);
    Task<Result<PauseReasonDetailDto>> CreatePauseReasonAsync(SavePauseReasonDto dto, CancellationToken ct = default);
    Task<Result<PauseReasonDetailDto>> UpdatePauseReasonAsync(long id, SavePauseReasonDto dto, CancellationToken ct = default);
    Task<Result<PauseReasonDetailDto>> SetPauseReasonActiveAsync(long id, bool isActive, CancellationToken ct = default);

    Task<IReadOnlyList<RoleDetailDto>> RolesAsync(CancellationToken ct = default);
    Task<Result<RoleDetailDto>> CreateRoleAsync(SaveRoleDto dto, CancellationToken ct = default);
    Task<Result<RoleDetailDto>> UpdateRoleAsync(long id, SaveRoleDto dto, CancellationToken ct = default);
    Task<Result<RoleDetailDto>> SetRolePermissionsAsync(long id, SetRolePermissionsDto dto, CancellationToken ct = default);
    Task<Result> DeleteRoleAsync(long id, CancellationToken ct = default);
}

/// <summary>
/// The reference data an administrator maintains: clients, departments, teams, pause reasons and
/// the roles themselves.
///
/// Two rules run through all of it.
///
/// **Nothing here is ever deleted once it has been used.** A client with requests against it, a
/// pause reason that appears in someone's timeline, a role somebody holds — removing any of them
/// would rewrite history that other screens still read, turning a report into a page of blanks. So
/// the operation offered is *deactivate*: the row stops being offered in new work and keeps
/// answering for the old. Only a role nobody holds and that was never seeded can actually be
/// deleted, because a role carries no history of its own.
///
/// **Names are unique, case-insensitively.** Two clients called "Falcon Traders" and "falcon
/// traders" are the same client to everyone except the database, and once requests are split
/// across both there is no honest way to merge them back.
/// </summary>
public sealed class SetupService : ISetupService
{
    private readonly IWorkflowDbContext _db;
    private readonly IAuditService _audit;

    public SetupService(IWorkflowDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // --- clients -----------------------------------------------------------------------------

    public async Task<IReadOnlyList<ClientDto>> ClientsAsync(CancellationToken ct = default) =>
        await _db.Clients.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ClientDto(
                c.Id, c.Name, c.Code, c.IsActive,
                _db.Requests.Count(r => r.ClientId == c.Id)))
            .ToListAsync(ct);

    public async Task<Result<ClientDto>> CreateClientAsync(SaveClientDto dto, CancellationToken ct = default)
    {
        var name = dto.Name.Trim();

        if (await _db.Clients.AnyAsync(c => c.Name.ToLower() == name.ToLower(), ct))
            return Duplicate<ClientDto>("client", name);

        var client = new Client { Name = name, Code = dto.Code?.Trim(), IsActive = true };
        _db.Clients.Add(client);

        _audit.Record(AuditActions.SetupChanged, entityType: nameof(Client),
            newValues: new { client.Name, client.Code });
        await _db.SaveChangesAsync(ct);

        return Result<ClientDto>.Success(new ClientDto(client.Id, client.Name, client.Code, true, 0));
    }

    public async Task<Result<ClientDto>> UpdateClientAsync(long id, SaveClientDto dto, CancellationToken ct = default)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return NotFound<ClientDto>("client");

        var name = dto.Name.Trim();

        if (await _db.Clients.AnyAsync(c => c.Id != id && c.Name.ToLower() == name.ToLower(), ct))
            return Duplicate<ClientDto>("client", name);

        var before = new { client.Name, client.Code };
        client.Name = name;
        client.Code = dto.Code?.Trim();

        _audit.Record(AuditActions.SetupChanged, entityType: nameof(Client), entityId: id,
            previousValues: before, newValues: new { client.Name, client.Code });
        await _db.SaveChangesAsync(ct);

        var used = await _db.Requests.CountAsync(r => r.ClientId == id, ct);
        return Result<ClientDto>.Success(new ClientDto(client.Id, client.Name, client.Code, client.IsActive, used));
    }

    public async Task<Result<ClientDto>> SetClientActiveAsync(long id, bool isActive, CancellationToken ct = default)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return NotFound<ClientDto>("client");

        client.IsActive = isActive;
        _audit.Record(AuditActions.SetupChanged, entityType: nameof(Client), entityId: id,
            previousValues: new { IsActive = !isActive }, newValues: new { IsActive = isActive });
        await _db.SaveChangesAsync(ct);

        var used = await _db.Requests.CountAsync(r => r.ClientId == id, ct);
        return Result<ClientDto>.Success(new ClientDto(client.Id, client.Name, client.Code, isActive, used));
    }

    // --- departments -------------------------------------------------------------------------

    public async Task<IReadOnlyList<DepartmentDto>> DepartmentsAsync(CancellationToken ct = default) =>
        await _db.Departments.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentDto(
                d.Id, d.Name, d.IsActive, _db.Teams.Count(t => t.DepartmentId == d.Id)))
            .ToListAsync(ct);

    public async Task<Result<DepartmentDto>> CreateDepartmentAsync(
        SaveDepartmentDto dto, CancellationToken ct = default)
    {
        var name = dto.Name.Trim();

        if (await _db.Departments.AnyAsync(d => d.Name.ToLower() == name.ToLower(), ct))
            return Duplicate<DepartmentDto>("department", name);

        var department = new Department { Name = name, IsActive = true };
        _db.Departments.Add(department);

        _audit.Record(AuditActions.SetupChanged, entityType: nameof(Department),
            newValues: new { department.Name });
        await _db.SaveChangesAsync(ct);

        return Result<DepartmentDto>.Success(new DepartmentDto(department.Id, department.Name, true, 0));
    }

    public async Task<Result<DepartmentDto>> UpdateDepartmentAsync(
        long id, SaveDepartmentDto dto, CancellationToken ct = default)
    {
        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null) return NotFound<DepartmentDto>("department");

        var name = dto.Name.Trim();

        if (await _db.Departments.AnyAsync(d => d.Id != id && d.Name.ToLower() == name.ToLower(), ct))
            return Duplicate<DepartmentDto>("department", name);

        var before = new { department.Name };
        department.Name = name;

        _audit.Record(AuditActions.SetupChanged, entityType: nameof(Department), entityId: id,
            previousValues: before, newValues: new { department.Name });
        await _db.SaveChangesAsync(ct);

        var teams = await _db.Teams.CountAsync(t => t.DepartmentId == id, ct);
        return Result<DepartmentDto>.Success(
            new DepartmentDto(department.Id, department.Name, department.IsActive, teams));
    }

    public async Task<Result<DepartmentDto>> SetDepartmentActiveAsync(
        long id, bool isActive, CancellationToken ct = default)
    {
        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null) return NotFound<DepartmentDto>("department");

        department.IsActive = isActive;
        _audit.Record(AuditActions.SetupChanged, entityType: nameof(Department), entityId: id,
            previousValues: new { IsActive = !isActive }, newValues: new { IsActive = isActive });
        await _db.SaveChangesAsync(ct);

        var teams = await _db.Teams.CountAsync(t => t.DepartmentId == id, ct);
        return Result<DepartmentDto>.Success(
            new DepartmentDto(department.Id, department.Name, isActive, teams));
    }

    // --- teams -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<TeamDto>> TeamsAsync(CancellationToken ct = default) =>
        await _db.Teams.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TeamDto(
                t.Id, t.Name, t.DepartmentId,
                _db.Departments.Where(d => d.Id == t.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                t.IsActive))
            .ToListAsync(ct);

    public async Task<Result<TeamDto>> CreateTeamAsync(SaveTeamDto dto, CancellationToken ct = default)
    {
        var name = dto.Name.Trim();

        if (await _db.Teams.AnyAsync(t => t.Name.ToLower() == name.ToLower(), ct))
            return Duplicate<TeamDto>("team", name);

        if (dto.DepartmentId is { } departmentId
            && !await _db.Departments.AnyAsync(d => d.Id == departmentId, ct))
            return Result<TeamDto>.Failure(
                Error.Validation("team.department_not_found", "That department does not exist."));

        var team = new Team { Name = name, DepartmentId = dto.DepartmentId, IsActive = true };
        _db.Teams.Add(team);

        _audit.Record(AuditActions.SetupChanged, entityType: nameof(Team),
            newValues: new { team.Name, team.DepartmentId });
        await _db.SaveChangesAsync(ct);

        return Result<TeamDto>.Success(await ProjectTeamAsync(team, ct));
    }

    public async Task<Result<TeamDto>> UpdateTeamAsync(long id, SaveTeamDto dto, CancellationToken ct = default)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return NotFound<TeamDto>("team");

        var name = dto.Name.Trim();

        if (await _db.Teams.AnyAsync(t => t.Id != id && t.Name.ToLower() == name.ToLower(), ct))
            return Duplicate<TeamDto>("team", name);

        var before = new { team.Name, team.DepartmentId };
        team.Name = name;
        team.DepartmentId = dto.DepartmentId;

        _audit.Record(AuditActions.SetupChanged, entityType: nameof(Team), entityId: id,
            previousValues: before, newValues: new { team.Name, team.DepartmentId });
        await _db.SaveChangesAsync(ct);

        return Result<TeamDto>.Success(await ProjectTeamAsync(team, ct));
    }

    public async Task<Result<TeamDto>> SetTeamActiveAsync(long id, bool isActive, CancellationToken ct = default)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return NotFound<TeamDto>("team");

        team.IsActive = isActive;
        _audit.Record(AuditActions.SetupChanged, entityType: nameof(Team), entityId: id,
            previousValues: new { IsActive = !isActive }, newValues: new { IsActive = isActive });
        await _db.SaveChangesAsync(ct);

        return Result<TeamDto>.Success(await ProjectTeamAsync(team, ct));
    }

    private async Task<TeamDto> ProjectTeamAsync(Team team, CancellationToken ct)
    {
        var departmentName = team.DepartmentId is { } id
            ? await _db.Departments.AsNoTracking()
                .Where(d => d.Id == id).Select(d => d.Name).FirstOrDefaultAsync(ct)
            : null;

        return new TeamDto(team.Id, team.Name, team.DepartmentId, departmentName, team.IsActive);
    }

    // --- pause reasons -----------------------------------------------------------------------

    public async Task<IReadOnlyList<PauseReasonDetailDto>> PauseReasonsAsync(CancellationToken ct = default) =>
        await _db.PauseReasons.AsNoTracking()
            .OrderBy(r => r.Category).ThenBy(r => r.Name)
            .Select(r => new PauseReasonDetailDto(
                r.Id, r.Name, r.RequiresComment, r.IsBlocker, r.Category, r.AwayState, r.IsActive,
                _db.WorkSessions.Count(s => s.EndPauseReasonId == r.Id)))
            .ToListAsync(ct);

    public async Task<Result<PauseReasonDetailDto>> CreatePauseReasonAsync(
        SavePauseReasonDto dto, CancellationToken ct = default)
    {
        var name = dto.Name.Trim();

        if (await _db.PauseReasons.AnyAsync(r => r.Name.ToLower() == name.ToLower(), ct))
            return Duplicate<PauseReasonDetailDto>("pause_reason", name);

        if (ValidateAwayState(dto.AwayState) is { } invalid)
            return Result<PauseReasonDetailDto>.Failure(invalid);

        var reason = new PauseReason
        {
            Name = name,
            RequiresComment = dto.RequiresComment,
            IsBlocker = dto.IsBlocker,
            Category = dto.Category,
            AwayState = dto.AwayState,
            IsActive = true,
        };

        _db.PauseReasons.Add(reason);
        _audit.Record(AuditActions.SetupChanged, entityType: nameof(PauseReason),
            newValues: Snapshot(reason));
        await _db.SaveChangesAsync(ct);

        return Result<PauseReasonDetailDto>.Success(Project(reason, 0));
    }

    public async Task<Result<PauseReasonDetailDto>> UpdatePauseReasonAsync(
        long id, SavePauseReasonDto dto, CancellationToken ct = default)
    {
        var reason = await _db.PauseReasons.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reason is null) return NotFound<PauseReasonDetailDto>("pause_reason");

        var name = dto.Name.Trim();

        if (await _db.PauseReasons.AnyAsync(r => r.Id != id && r.Name.ToLower() == name.ToLower(), ct))
            return Duplicate<PauseReasonDetailDto>("pause_reason", name);

        if (ValidateAwayState(dto.AwayState) is { } invalid)
            return Result<PauseReasonDetailDto>.Failure(invalid);

        var before = Snapshot(reason);

        reason.Name = name;
        reason.RequiresComment = dto.RequiresComment;
        reason.IsBlocker = dto.IsBlocker;
        reason.Category = dto.Category;
        reason.AwayState = dto.AwayState;

        _audit.Record(AuditActions.SetupChanged, entityType: nameof(PauseReason), entityId: id,
            previousValues: before, newValues: Snapshot(reason));
        await _db.SaveChangesAsync(ct);

        var used = await _db.WorkSessions.CountAsync(s => s.EndPauseReasonId == id, ct);
        return Result<PauseReasonDetailDto>.Success(Project(reason, used));
    }

    public async Task<Result<PauseReasonDetailDto>> SetPauseReasonActiveAsync(
        long id, bool isActive, CancellationToken ct = default)
    {
        var reason = await _db.PauseReasons.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reason is null) return NotFound<PauseReasonDetailDto>("pause_reason");

        reason.IsActive = isActive;
        _audit.Record(AuditActions.SetupChanged, entityType: nameof(PauseReason), entityId: id,
            previousValues: new { IsActive = !isActive }, newValues: new { IsActive = isActive });
        await _db.SaveChangesAsync(ct);

        var used = await _db.WorkSessions.CountAsync(s => s.EndPauseReasonId == id, ct);
        return Result<PauseReasonDetailDto>.Success(Project(reason, used));
    }

    /// <summary>
    /// <c>ShiftEnded</c> is reachable only through the end-shift operation — that is what makes a
    /// shift's end time trustworthy. A pause reason that set it would end someone's day from the
    /// task screen, leaving the shift closed with a work session still open.
    /// </summary>
    private static Error? ValidateAwayState(WorkforceState? state) =>
        state == WorkforceState.ShiftEnded
            ? Error.Validation(
                "pause_reason.invalid_away_state",
                "A pause reason cannot end someone's shift. Use one of the away states instead.")
            : null;

    private static object Snapshot(PauseReason r) =>
        new { r.Name, r.RequiresComment, r.IsBlocker, r.Category, r.AwayState };

    private static PauseReasonDetailDto Project(PauseReason r, int used) =>
        new(r.Id, r.Name, r.RequiresComment, r.IsBlocker, r.Category, r.AwayState, r.IsActive, used);

    // --- roles -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<RoleDetailDto>> RolesAsync(CancellationToken ct = default) =>
        await _db.Roles.AsNoTracking()
            .OrderByDescending(r => r.IsSystemRole).ThenBy(r => r.Name)
            .Select(r => new RoleDetailDto(
                r.Id, r.Name, r.Description, r.IsSystemRole,
                r.UserRoles.Count,
                r.RolePermissions
                    .Select(rp => _db.Permissions.Where(p => p.Id == rp.PermissionId)
                        .Select(p => p.Key).FirstOrDefault()!)
                    .Where(k => k != null)
                    .ToList()))
            .ToListAsync(ct);

    public async Task<Result<RoleDetailDto>> CreateRoleAsync(SaveRoleDto dto, CancellationToken ct = default)
    {
        var name = dto.Name.Trim();

        if (await _db.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower(), ct))
            return Duplicate<RoleDetailDto>("role", name);

        // A role created here is never a system role: those are the seeder's, and marking a
        // hand-made one as system would make it undeletable for no reason.
        var role = new Role { Name = name, Description = dto.Description?.Trim(), IsSystemRole = false };
        _db.Roles.Add(role);

        _audit.Record(AuditActions.RoleChanged, entityType: nameof(Role),
            newValues: new { role.Name, role.Description });
        await _db.SaveChangesAsync(ct);

        return Result<RoleDetailDto>.Success(
            new RoleDetailDto(role.Id, role.Name, role.Description, false, 0, Array.Empty<string>()));
    }

    public async Task<Result<RoleDetailDto>> UpdateRoleAsync(
        long id, SaveRoleDto dto, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound<RoleDetailDto>("role");

        var name = dto.Name.Trim();

        // A system role's *name* is load-bearing: DefaultRoles.Map is keyed by it, and the seeder
        // re-creates any role it cannot find. Renaming "Administrator" would spawn a second one on
        // the next restart and leave the first without its grants. The description is free.
        if (role.IsSystemRole && !role.Name.Equals(name, StringComparison.Ordinal))
            return Result<RoleDetailDto>.Failure(Error.Validation(
                "role.system_rename",
                $"'{role.Name}' is a built-in role and cannot be renamed. You can change what it "
                + "grants, or add a role of your own."));

        if (await _db.Roles.AnyAsync(r => r.Id != id && r.Name.ToLower() == name.ToLower(), ct))
            return Duplicate<RoleDetailDto>("role", name);

        var before = new { role.Name, role.Description };
        role.Name = name;
        role.Description = dto.Description?.Trim();

        _audit.Record(AuditActions.RoleChanged, entityType: nameof(Role), entityId: id,
            previousValues: before, newValues: new { role.Name, role.Description });
        await _db.SaveChangesAsync(ct);

        return await ReadRoleAsync(id, ct);
    }

    public async Task<Result<RoleDetailDto>> SetRolePermissionsAsync(
        long id, SetRolePermissionsDto dto, CancellationToken ct = default)
    {
        var role = await _db.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (role is null) return NotFound<RoleDetailDto>("role");

        var wanted = dto.Permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var known = await _db.Permissions.AsNoTracking()
            .Where(p => wanted.Contains(p.Key))
            .Select(p => new { p.Id, p.Key })
            .ToListAsync(ct);

        if (known.Count != wanted.Count)
        {
            var missing = wanted.Except(known.Select(k => k.Key), StringComparer.OrdinalIgnoreCase);
            return Result<RoleDetailDto>.Failure(Error.Validation(
                "role.unknown_permission",
                $"Unknown permission(s): {string.Join(", ", missing)}."));
        }

        // The last way back in. An administrator who removes Admin.ManageRoles from the only role
        // that has it locks the permission editor for everyone, permanently, with no way to undo it
        // from inside the application. Refusing is the only recovery that does not involve SQL.
        if (await WouldOrphanAsync(role, known.Select(k => k.Key).ToList(), Permissions.AdminManageRoles, ct))
            return Result<RoleDetailDto>.Failure(Error.Validation(
                "role.last_administrator",
                "This is the only role that can manage roles, and someone holds it. Removing that "
                + "permission would lock everyone out of this screen for good."));

        var before = role.RolePermissions
            .Select(rp => _db.Permissions.Where(p => p.Id == rp.PermissionId).Select(p => p.Key).First())
            .OrderBy(k => k).ToList();

        _db.RolePermissions.RemoveRange(role.RolePermissions);

        foreach (var permission in known)
            _db.RolePermissions.Add(new RolePermission { RoleId = id, PermissionId = permission.Id });

        _audit.Record(AuditActions.RoleChanged, entityType: nameof(Role), entityId: id,
            previousValues: new { Permissions = before },
            newValues: new { Permissions = known.Select(k => k.Key).OrderBy(k => k).ToList() });

        await _db.SaveChangesAsync(ct);
        return await ReadRoleAsync(id, ct);
    }

    public async Task<Result> DeleteRoleAsync(long id, CancellationToken ct = default)
    {
        var role = await _db.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (role is null)
            return Result.Failure(Error.NotFound("role.not_found", "Role not found."));

        if (role.IsSystemRole)
            return Result.Failure(Error.Validation(
                "role.system_delete",
                $"'{role.Name}' is a built-in role. The seeder would recreate it on the next "
                + "restart, so removing it here would only look like it worked."));

        var holders = await _db.UserRoles.CountAsync(ur => ur.RoleId == id, ct);
        if (holders > 0)
            return Result.Failure(Error.Conflict(
                "role.in_use",
                $"{holders} " + (holders == 1 ? "person holds" : "people hold") + " this role. "
                + "Take it off them first — deleting it would silently remove what they can do."));

        _db.RolePermissions.RemoveRange(role.RolePermissions);
        _db.Roles.Remove(role);

        _audit.Record(AuditActions.RoleChanged, entityType: nameof(Role), entityId: id,
            previousValues: new { role.Name }, newValues: null);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }

    /// <summary>
    /// True when this role is the last held route to <paramref name="permission"/> and the change
    /// would drop it. A role nobody holds cannot orphan anything, so it is free to edit.
    /// </summary>
    private async Task<bool> WouldOrphanAsync(
        Role role, IReadOnlyList<string> wanted, string permission, CancellationToken ct)
    {
        if (wanted.Contains(permission, StringComparer.OrdinalIgnoreCase)) return false;

        var hasIt = role.RolePermissions
            .Select(rp => _db.Permissions.Where(p => p.Id == rp.PermissionId).Select(p => p.Key).First())
            .Any(k => k.Equals(permission, StringComparison.OrdinalIgnoreCase));

        if (!hasIt) return false;
        if (!await _db.UserRoles.AnyAsync(ur => ur.RoleId == role.Id, ct)) return false;

        var otherHeldRoutes = await _db.RolePermissions
            .Where(rp => rp.RoleId != role.Id)
            .Where(rp => _db.Permissions.Any(p => p.Id == rp.PermissionId && p.Key == permission))
            .Where(rp => _db.UserRoles.Any(ur => ur.RoleId == rp.RoleId))
            .AnyAsync(ct);

        return !otherHeldRoutes;
    }

    private async Task<Result<RoleDetailDto>> ReadRoleAsync(long id, CancellationToken ct)
    {
        var role = (await RolesAsync(ct)).FirstOrDefault(r => r.Id == id);

        return role is null
            ? NotFound<RoleDetailDto>("role")
            : Result<RoleDetailDto>.Success(role);
    }

    // --- shared failures ---------------------------------------------------------------------

    private static Result<T> NotFound<T>(string what) =>
        Result<T>.Failure(Error.NotFound($"{what}.not_found", "That entry no longer exists."));

    private static Result<T> Duplicate<T>(string what, string name) =>
        Result<T>.Failure(Error.Conflict(
            $"{what}.duplicate_name", $"'{name}' already exists. Names have to be unique."));
}
