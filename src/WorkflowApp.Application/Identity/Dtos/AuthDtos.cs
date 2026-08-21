using System.ComponentModel.DataAnnotations;

namespace WorkflowApp.Application.Identity.Dtos;

public sealed record LoginRequest
{
    [Required, MaxLength(256)]
    public string UserName { get; init; } = default!;

    [Required, MaxLength(200)]
    public string Password { get; init; } = default!;
}

public sealed record RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; init; } = default!;
}

public sealed record ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; init; } = default!;

    [Required, MaxLength(200)]
    public string NewPassword { get; init; } = default!;
}

public sealed record CreateUserRequest
{
    [Required, MaxLength(100)]
    public string UserName { get; init; } = default!;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = default!;

    [Required, MaxLength(200)]
    public string DisplayName { get; init; } = default!;

    [Required, MaxLength(200)]
    public string Password { get; init; } = default!;

    public long? DepartmentId { get; init; }
    public long? TeamId { get; init; }

    /// <summary>Role names from <see cref="Application.Common.DefaultRoles"/> or custom roles.</summary>
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}

public sealed record AssignRolesRequest
{
    [Required]
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}

public sealed record ResetPasswordRequest
{
    [Required, MaxLength(200)]
    public string NewPassword { get; init; } = default!;
}

public sealed record SetActiveRequest
{
    public bool IsActive { get; init; }
}

/// <summary>Identity projection returned to clients. Never carries the password hash.</summary>
public sealed record UserDto(
    long Id,
    string UserName,
    string Email,
    string DisplayName,
    bool IsActive,
    string WorkforceState,
    DateTimeOffset? LastLoginAt,
    long? DepartmentId,
    long? TeamId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);

public sealed record RoleDto(long Id, string Name, string? Description, bool IsSystemRole, IReadOnlyList<string> Permissions);
