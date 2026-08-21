using WorkflowApp.Domain.Common;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Domain.Entities.Identity;

public class User : BaseEntity
{
    public string UserName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;

    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEndAt { get; set; }

    public long? DepartmentId { get; set; }
    public long? TeamId { get; set; }

    /// <summary>Current live availability. Updated by the workforce state machine (Phase 2).</summary>
    public WorkforceState WorkforceState { get; set; } = WorkforceState.NotLoggedIn;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public class Role : BaseEntity
{
    public string Name { get; set; } = default!;         // e.g. "Reviewer", "AssignmentManager"
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }               // seeded roles that can't be deleted

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

/// <summary>Fine-grained permission, e.g. "Task.Assign". Roles bundle these.</summary>
public class Permission : BaseEntity
{
    public string Key { get; set; } = default!;          // unique, e.g. "Task.QCReview"
    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class UserRole
{
    public long UserId { get; set; }
    public User User { get; set; } = default!;
    public long RoleId { get; set; }
    public Role Role { get; set; } = default!;
}

public class RolePermission
{
    public long RoleId { get; set; }
    public Role Role { get; set; } = default!;
    public long PermissionId { get; set; }
    public Permission Permission { get; set; } = default!;
}

/// <summary>Security/audit trail for authentication attempts.</summary>
public class LoginAttempt : BaseEntity
{
    public string UserNameTried { get; set; } = default!;
    public bool Succeeded { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? FailureReason { get; set; }
}

/// <summary>Refresh-token record for rotation/revocation (Phase 1).</summary>
public class RefreshToken : BaseEntity
{
    public long UserId { get; set; }
    public User User { get; set; } = default!;
    public string TokenHash { get; set; } = default!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}
