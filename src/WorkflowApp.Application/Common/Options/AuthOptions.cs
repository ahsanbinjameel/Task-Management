namespace WorkflowApp.Application.Common.Options;

/// <summary>Bound from the <c>Jwt</c> configuration section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "WorkflowApp";
    public string Audience { get; set; } = "WorkflowApp";
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>
    /// HMAC signing key. Must be at least 32 bytes. Supply via environment variable or user-secrets
    /// in every environment other than local development.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;
}

/// <summary>Bound from the <c>Auth</c> configuration section. Account-protection policy.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Consecutive failed logins before the account is locked.</summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>How long a lockout lasts.</summary>
    public int LockoutMinutes { get; set; } = 15;

    public int MinimumPasswordLength { get; set; } = 10;

    /// <summary>Seeded on an empty database so there is a way in. Change it immediately after.</summary>
    public string DefaultAdminUserName { get; set; } = "admin";
    public string DefaultAdminEmail { get; set; } = "admin@workflowapp.local";
    public string DefaultAdminDisplayName { get; set; } = "System Administrator";
    public string DefaultAdminPassword { get; set; } = "ChangeMe!2024";
}
