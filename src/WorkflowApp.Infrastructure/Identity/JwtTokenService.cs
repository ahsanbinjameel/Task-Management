using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Domain.Entities.Identity;

namespace WorkflowApp.Infrastructure.Identity;

/// <summary>Claim types this application issues and reads. Shared by the token service and the API.</summary>
public static class AppClaimTypes
{
    /// <summary>One claim per effective permission key, e.g. <c>permission: Task.Assign</c>.</summary>
    public const string Permission = "permission";

    public const string DisplayName = "display_name";

    /// <summary>
    /// Marks a demo session. Its presence is what points this request's DbContext at the demo
    /// catalog instead of the live one, which is why it rides on the signed token and nowhere
    /// else — a header would let any client choose which database it writes to.
    /// </summary>
    public const string Demo = "demo";

    /// <summary>
    /// The real account that started the demonstration, so exiting can hand them their own session
    /// back. It names somebody in the <em>live</em> catalog, deliberately: the demo user is who the
    /// app behaves as, this is who is actually sitting there.
    /// </summary>
    public const string DemoRealUser = "demo_real_user";

    public const string DemoRealUserName = "demo_real_user_name";
}

/// <summary>
/// Issues signed JWT access tokens and opaque refresh tokens.
///
/// Effective permissions are embedded as claims so authorization needs no database round trip per
/// request. The trade-off: a permission change only takes effect on the next token issue, which is
/// why access-token lifetime is kept short (default 30 minutes).
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private const int RefreshTokenBytes = 32;

    private readonly JwtOptions _options;
    private readonly IDateTimeProvider _clock;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IOptions<JwtOptions> options, IDateTimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey ?? string.Empty);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 bytes (32 ASCII characters). " +
                "Supply it via environment variable or user-secrets — never commit a real key.");
        }

        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
    }

    public AccessToken CreateAccessToken(
        User user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions,
        bool isDemo = false,
        long? demoRealUserId = null,
        string? demoRealUserName = null)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(AppClaimTypes.DisplayName, user.DisplayName)
        };

        // Email is optional. Claim values may not be null, so the claim is omitted entirely rather
        // than carrying an empty string that consumers would have to special-case anyway.
        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(ClaimTypes.Email, user.Email));

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim(AppClaimTypes.Permission, p)));

        // Note what a demo token does *not* get: any of the real administrator's permissions. It
        // is a token for the demo user, with their claims alone, so the demonstration shows what
        // that role actually sees rather than an administrator's view under a different name.
        if (isDemo)
        {
            claims.Add(new Claim(AppClaimTypes.Demo, "true"));

            if (demoRealUserId is { } realUserId)
                claims.Add(new Claim(AppClaimTypes.DemoRealUser, realUserId.ToString()));

            if (!string.IsNullOrWhiteSpace(demoRealUserName))
                claims.Add(new Claim(AppClaimTypes.DemoRealUserName, demoRealUserName));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _signingCredentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public IssuedRefreshToken CreateRefreshToken()
    {
        // Opaque and high-entropy: a refresh token carries no claims, it is only a database lookup key.
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
        return new IssuedRefreshToken(
            raw,
            HashRefreshToken(raw),
            _clock.UtcNow.AddDays(_options.RefreshTokenDays));
    }

    /// <summary>
    /// SHA-256 of the raw token. Plain hashing (no salt/stretching) is correct here: the input is
    /// 256 bits of cryptographic randomness, so there is nothing to brute-force. Storing hashes
    /// means a database leak does not hand over usable tokens.
    /// </summary>
    public string HashRefreshToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
