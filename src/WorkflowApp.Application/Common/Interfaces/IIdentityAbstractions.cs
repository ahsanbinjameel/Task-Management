using WorkflowApp.Domain.Entities.Identity;

namespace WorkflowApp.Application.Common.Interfaces;

/// <summary>Ambient information about the caller, supplied by the API layer from the JWT.</summary>
public interface ICurrentUser
{
    long? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }

    /// <summary>Effective permissions carried as claims on the access token.</summary>
    IReadOnlySet<string> Permissions { get; }

    string? IpAddress { get; }
    string? UserAgent { get; }
}

/// <summary>Clock abstraction so time-dependent rules (lockout, expiry) are testable.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Outcome of verifying a supplied password against a stored hash.</summary>
public enum PasswordVerification
{
    Failed = 0,
    Success = 1,
    /// <summary>Correct password, but the stored hash uses outdated parameters — rehash it.</summary>
    SuccessRehashNeeded = 2
}

/// <summary>
/// Password hashing, kept behind an interface so the Application layer does not depend on
/// ASP.NET Core Identity. The Infrastructure adapter wraps Identity's PBKDF2 hasher.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    PasswordVerification Verify(string passwordHash, string providedPassword);
}

/// <summary>An issued JWT access token and the instant it stops being valid.</summary>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>An issued refresh token: the raw value goes to the client, the hash to the database.</summary>
public sealed record IssuedRefreshToken(string RawToken, string TokenHash, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user, IEnumerable<string> roles, IEnumerable<string> permissions);

    /// <summary>Generates a cryptographically random refresh token plus its storable hash.</summary>
    IssuedRefreshToken CreateRefreshToken();

    /// <summary>Hashes a raw refresh token so an incoming value can be matched against storage.</summary>
    string HashRefreshToken(string rawToken);
}
