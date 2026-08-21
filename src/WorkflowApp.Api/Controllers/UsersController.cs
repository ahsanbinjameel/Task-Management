using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Identity.Dtos;
using WorkflowApp.Application.Identity.Services;

namespace WorkflowApp.Api.Controllers;

/// <summary>
/// Administrative user management. Every action is permission-gated server-side — this is the
/// security boundary, regardless of what the UI chooses to show.
/// </summary>
[HasPermission(Permissions.AdminManageUsers)]
public sealed class UsersController : ApiControllerBase
{
    private readonly IUserAdminService _users;

    public UsersController(IUserAdminService users) => _users = users;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        CancellationToken ct = default)
    {
        var result = await _users.ListUsersAsync(
            new PageQuery { Page = page, PageSize = pageSize }, search, isActive, ct);

        return Ok(result);
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
        => FromResult(await _users.GetUserAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var result = await _users.CreateUserAsync(request, ct);
        if (result.IsFailure)
            return Problem(result.Error!);

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>Activates or deactivates an account. Deactivation revokes live refresh tokens.</summary>
    [HttpPut("{id:long}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActive(long id, [FromBody] SetActiveRequest request, CancellationToken ct)
        => FromResult(await _users.SetActiveAsync(id, request.IsActive, ct));

    /// <summary>Replaces the user's roles with exactly the set supplied.</summary>
    [HttpPut("{id:long}/roles")]
    [HasPermission(Permissions.AdminManageRoles)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AssignRoles(long id, [FromBody] AssignRolesRequest request, CancellationToken ct)
        => FromResult(await _users.AssignRolesAsync(id, request.Roles, ct));

    /// <summary>Administrative password reset. Clears any lockout and revokes live refresh tokens.</summary>
    [HttpPost("{id:long}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(long id, [FromBody] ResetPasswordRequest request, CancellationToken ct)
        => FromResult(await _users.ResetPasswordAsync(id, request.NewPassword, ct));
}

/// <summary>Read-only view of roles and the permissions each one grants.</summary>
[HasPermission(Permissions.AdminManageRoles)]
public sealed class RolesController : ApiControllerBase
{
    private readonly IUserAdminService _users;

    public RolesController(IUserAdminService users) => _users = users;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _users.ListRolesAsync(ct));

    /// <summary>The full permission catalog, for building role-editing screens.</summary>
    [HttpGet("permissions")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public IActionResult ListPermissions() => Ok(Permissions.All);
}
