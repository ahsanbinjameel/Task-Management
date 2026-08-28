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

// --- the product catalog (PRODUCT-CORE §5) -------------------------------------------------------
//
// Module → Form → Surface, and no reference to Client anywhere in it. Your product has these; each
// client runs an instance of it. Modelling them per client would give every client a private copy
// of the same form and make "which forms generate the most support" unanswerable.

/// <summary>A part of the product. <c>Forms</c> is how many hang off it, for the retire warning.</summary>
public sealed record ModuleDto(long Id, string Name, bool IsActive, int Forms, int UsedBy);

public sealed record SaveModuleDto
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = default!;
}

/// <summary>A screen or document inside a module.</summary>
public sealed record FormDto(
    long Id, string Name, long ModuleId, string ModuleName, bool IsActive, int Surfaces, int UsedBy);

public sealed record SaveFormDto
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = default!;

    [Required]
    public long ModuleId { get; init; }
}

/// <summary>A way of looking at a form: the form itself, History, Detail Report, Master Report.</summary>
public sealed record FormSurfaceDto(
    long Id, string Name, long FormId, string FormName, string ModuleName, bool IsActive, int UsedBy);

public sealed record SaveFormSurfaceDto
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = default!;

    [Required]
    public long FormId { get; init; }
}
