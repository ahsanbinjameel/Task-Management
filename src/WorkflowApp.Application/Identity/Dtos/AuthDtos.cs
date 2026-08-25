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

    /// <summary>Optional — staff are identified by username / employee code, not by email.</summary>
    [EmailAddress, MaxLength(256)]
    public string? Email { get; init; }

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

/// <summary>
/// Everything about an account except its roles, in one edit.
///
/// The username is editable. An earlier version refused, on the grounds that history was recorded
/// against it — that was wrong: <c>AuditLog.ActorUserId</c> and every other back-reference is the
/// numeric id, so a rename carries the whole trail with it. The one thing that does not follow is
/// <c>LoginAttempt.UserNameTried</c>, which is a record of what was actually typed at the time and
/// is supposed to keep the old value.
///
/// <see cref="NewPassword"/> is optional and means "leave it alone" when blank — an administrator
/// correcting a surname must not have to reissue a password to do it.
/// </summary>
public sealed record UpdateUserRequest
{
    [Required, MaxLength(100)]
    public string UserName { get; init; } = default!;

    [Required, MaxLength(200)]
    public string DisplayName { get; init; } = default!;

    [EmailAddress, MaxLength(256)]
    public string? Email { get; init; }

    /// <summary>Blank leaves the password unchanged. Setting one signs the person out everywhere.</summary>
    [MaxLength(200)]
    public string? NewPassword { get; init; }

    public long? DepartmentId { get; init; }
    public long? TeamId { get; init; }
}

/// <summary>What a person may change about their own account, without an administrator.</summary>
public sealed record UpdateProfileRequest
{
    [Required, MaxLength(200)]
    public string DisplayName { get; init; } = default!;

    [EmailAddress, MaxLength(256)]
    public string? Email { get; init; }
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
    string? Email,
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
