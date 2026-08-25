using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Admin.Dtos;
using WorkflowApp.Application.Admin.Services;
using WorkflowApp.Application.Common;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// The reference data an administrator maintains.
///
/// Gated on <see cref="Permissions.AdminManageConfig"/> — a permission that was in the catalogue
/// from the beginning ("Manage system configuration and lookup data") and, until this controller,
/// granted to the Administrator role and enforced nowhere. Reading this data stays open to any
/// signed-in caller through the existing lookup endpoints; only changing it lands here.
///
/// Note what is missing: DELETE, for everything except an unused role. Reference data is pointed at
/// by history, so it is deactivated rather than removed. <see cref="ISetupService"/> explains why.
/// </summary>
[Route("api/setup")]
[HasPermission(Permissions.AdminManageConfig)]
public sealed class SetupController : ApiControllerBase
{
    private readonly ISetupService _setup;

    public SetupController(ISetupService setup) => _setup = setup;

    // --- clients -----------------------------------------------------------------------------

    [HttpGet("clients")]
    [ProducesResponseType(typeof(IReadOnlyList<ClientDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Clients(CancellationToken ct)
        => Ok(await _setup.ClientsAsync(ct));

    [HttpPost("clients")]
    public async Task<IActionResult> CreateClient([FromBody] SaveClientDto dto, CancellationToken ct)
        => FromResult(await _setup.CreateClientAsync(dto, ct));

    [HttpPut("clients/{id:long}")]
    public async Task<IActionResult> UpdateClient(long id, [FromBody] SaveClientDto dto, CancellationToken ct)
        => FromResult(await _setup.UpdateClientAsync(id, dto, ct));

    [HttpPut("clients/{id:long}/active")]
    public async Task<IActionResult> SetClientActive(long id, [FromBody] SetActiveDto dto, CancellationToken ct)
        => FromResult(await _setup.SetClientActiveAsync(id, dto.IsActive, ct));

    // --- departments -------------------------------------------------------------------------

    [HttpGet("departments")]
    [ProducesResponseType(typeof(IReadOnlyList<DepartmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Departments(CancellationToken ct)
        => Ok(await _setup.DepartmentsAsync(ct));

    [HttpPost("departments")]
    public async Task<IActionResult> CreateDepartment([FromBody] SaveDepartmentDto dto, CancellationToken ct)
        => FromResult(await _setup.CreateDepartmentAsync(dto, ct));

    [HttpPut("departments/{id:long}")]
    public async Task<IActionResult> UpdateDepartment(
        long id, [FromBody] SaveDepartmentDto dto, CancellationToken ct)
        => FromResult(await _setup.UpdateDepartmentAsync(id, dto, ct));

    [HttpPut("departments/{id:long}/active")]
    public async Task<IActionResult> SetDepartmentActive(
        long id, [FromBody] SetActiveDto dto, CancellationToken ct)
        => FromResult(await _setup.SetDepartmentActiveAsync(id, dto.IsActive, ct));

    // --- teams -------------------------------------------------------------------------------

    [HttpGet("teams")]
    [ProducesResponseType(typeof(IReadOnlyList<TeamDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Teams(CancellationToken ct)
        => Ok(await _setup.TeamsAsync(ct));

    [HttpPost("teams")]
    public async Task<IActionResult> CreateTeam([FromBody] SaveTeamDto dto, CancellationToken ct)
        => FromResult(await _setup.CreateTeamAsync(dto, ct));

    [HttpPut("teams/{id:long}")]
    public async Task<IActionResult> UpdateTeam(long id, [FromBody] SaveTeamDto dto, CancellationToken ct)
        => FromResult(await _setup.UpdateTeamAsync(id, dto, ct));

    [HttpPut("teams/{id:long}/active")]
    public async Task<IActionResult> SetTeamActive(long id, [FromBody] SetActiveDto dto, CancellationToken ct)
        => FromResult(await _setup.SetTeamActiveAsync(id, dto.IsActive, ct));

    // --- pause reasons -----------------------------------------------------------------------

    [HttpGet("pause-reasons")]
    [ProducesResponseType(typeof(IReadOnlyList<PauseReasonDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PauseReasons(CancellationToken ct)
        => Ok(await _setup.PauseReasonsAsync(ct));

    [HttpPost("pause-reasons")]
    public async Task<IActionResult> CreatePauseReason([FromBody] SavePauseReasonDto dto, CancellationToken ct)
        => FromResult(await _setup.CreatePauseReasonAsync(dto, ct));

    [HttpPut("pause-reasons/{id:long}")]
    public async Task<IActionResult> UpdatePauseReason(
        long id, [FromBody] SavePauseReasonDto dto, CancellationToken ct)
        => FromResult(await _setup.UpdatePauseReasonAsync(id, dto, ct));

    [HttpPut("pause-reasons/{id:long}/active")]
    public async Task<IActionResult> SetPauseReasonActive(
        long id, [FromBody] SetActiveDto dto, CancellationToken ct)
        => FromResult(await _setup.SetPauseReasonActiveAsync(id, dto.IsActive, ct));

    // --- roles -------------------------------------------------------------------------------
    //
    // Roles are configuration, but they are also the authorization model, so they carry their own
    // permission: someone who maintains the client list has no business rewriting what a role can
    // do. Both are held by Administrator by default.

    [HttpGet("roles")]
    [HasPermission(Permissions.AdminManageRoles)]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Roles(CancellationToken ct)
        => Ok(await _setup.RolesAsync(ct));

    [HttpPost("roles")]
    [HasPermission(Permissions.AdminManageRoles)]
    public async Task<IActionResult> CreateRole([FromBody] SaveRoleDto dto, CancellationToken ct)
        => FromResult(await _setup.CreateRoleAsync(dto, ct));

    [HttpPut("roles/{id:long}")]
    [HasPermission(Permissions.AdminManageRoles)]
    public async Task<IActionResult> UpdateRole(long id, [FromBody] SaveRoleDto dto, CancellationToken ct)
        => FromResult(await _setup.UpdateRoleAsync(id, dto, ct));

    [HttpPut("roles/{id:long}/permissions")]
    [HasPermission(Permissions.AdminManageRoles)]
    public async Task<IActionResult> SetRolePermissions(
        long id, [FromBody] SetRolePermissionsDto dto, CancellationToken ct)
        => FromResult(await _setup.SetRolePermissionsAsync(id, dto, ct));

    [HttpDelete("roles/{id:long}")]
    [HasPermission(Permissions.AdminManageRoles)]
    public async Task<IActionResult> DeleteRole(long id, CancellationToken ct)
        => FromResult(await _setup.DeleteRoleAsync(id, ct));
}

/// <summary>Shared by every activate/deactivate endpoint above.</summary>
public sealed record SetActiveDto
{
    public bool IsActive { get; init; }
}
