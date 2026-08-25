using System.ComponentModel.DataAnnotations;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Admin.Dtos;

/// <summary>
/// The reference data an administrator maintains, and the shape it is edited in.
///
/// Deliberately one file: these are half a dozen small, near-identical lists, and a DTO file each
/// would be five files of ceremony around three properties.
/// </summary>

// --- clients -------------------------------------------------------------------------------

public sealed record ClientDto(long Id, string Name, string? Code, bool IsActive, int RequestCount);

public sealed record SaveClientDto
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = default!;

    [MaxLength(50)]
    public string? Code { get; init; }
}

// --- departments and teams -----------------------------------------------------------------

public sealed record DepartmentDto(long Id, string Name, bool IsActive, int TeamCount);

public sealed record TeamDto(long Id, string Name, long? DepartmentId, string? DepartmentName, bool IsActive);

public sealed record SaveDepartmentDto
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = default!;
}

public sealed record SaveTeamDto
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = default!;

    public long? DepartmentId { get; init; }
}

// --- pause reasons -------------------------------------------------------------------------

/// <summary>
/// A pause reason carries more than a name, and every field changes behaviour rather than wording:
/// <see cref="IsBlocker"/> decides whether the task is genuinely stuck, and <see cref="AwayState"/>
/// decides whether the person also steps off the floor. The editor has to explain both, because an
/// administrator setting "Waiting for client" to Break would quietly corrupt attendance.
/// </summary>
public sealed record PauseReasonDetailDto(
    long Id,
    string Name,
    bool RequiresComment,
    bool IsBlocker,
    PauseCategory Category,
    WorkforceState? AwayState,
    bool IsActive,
    int TimesUsed);

public sealed record SavePauseReasonDto
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = default!;

    public bool RequiresComment { get; init; }
    public bool IsBlocker { get; init; }
    public PauseCategory Category { get; init; } = PauseCategory.Other;

    /// <summary>Where the person goes, if anywhere. Never <c>ShiftEnded</c> — see the entity.</summary>
    public WorkforceState? AwayState { get; init; }
}

// --- roles ---------------------------------------------------------------------------------

public sealed record RoleDetailDto(
    long Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    int UserCount,
    IReadOnlyList<string> Permissions);

public sealed record SaveRoleDto
{
    [Required, MaxLength(100)]
    public string Name { get; init; } = default!;

    [MaxLength(500)]
    public string? Description { get; init; }
}

public sealed record SetRolePermissionsDto
{
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
}
