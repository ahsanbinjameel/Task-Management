using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Application.Identity.Services;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Infrastructure.Identity;
using Xunit;

namespace WorkflowApp.Application.Tests;

public class JwtTokenServiceTests
{
    private static readonly User SampleUser = new()
    {
        Id = 42,
        UserName = "worker1",
        Email = "worker1@workflowapp.local",
        DisplayName = "Worker One"
    };

    private static JwtTokenService CreateService(FixedClock clock, string? signingKey = null) =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "WorkflowApp.Tests",
            Audience = "WorkflowApp.Tests",
            AccessTokenMinutes = 30,
            RefreshTokenDays = 14,
            SigningKey = signingKey ?? TestHarness.TestSigningKey
        }), clock);

    [Fact]
    public void Access_token_carries_identity_roles_and_permission_claims()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var token = CreateService(clock).CreateAccessToken(
            SampleUser,
            new[] { DefaultRoles.Worker },
            new[] { Permissions.TaskWork, Permissions.RequestCreate });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);

        Assert.Equal("42", jwt.Claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("worker1", jwt.Claims.Single(c => c.Type == ClaimTypes.Name).Value);
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Role && c.Value == DefaultRoles.Worker);

        var permissions = jwt.Claims.Where(c => c.Type == AppClaimTypes.Permission).Select(c => c.Value).ToList();
        Assert.Contains(Permissions.TaskWork, permissions);
        Assert.Contains(Permissions.RequestCreate, permissions);
        Assert.DoesNotContain(Permissions.AdminManageUsers, permissions);
    }

    [Fact]
    public void Access_token_expiry_follows_the_configured_lifetime()
    {
        var start = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var token = CreateService(new FixedClock(start))
            .CreateAccessToken(SampleUser, Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(start.AddMinutes(30), token.ExpiresAt);
    }

    [Fact]
    public void Access_token_validates_against_the_issuing_key_and_fails_against_another()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var token = CreateService(clock)
            .CreateAccessToken(SampleUser, Array.Empty<string>(), new[] { Permissions.TaskWork });

        var handler = new JwtSecurityTokenHandler();

        TokenValidationParameters Parameters(string key) => new()
        {
            ValidIssuer = "WorkflowApp.Tests",
            ValidAudience = "WorkflowApp.Tests",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero
        };

        var principal = handler.ValidateToken(token.Token, Parameters(TestHarness.TestSigningKey), out _);
        Assert.Equal("42", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);

        // A token signed with a different key must not validate — this is the whole security boundary.
        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(token.Token, Parameters("a-completely-different-key-32-bytes-long"), out _));
    }

    [Fact]
    public void Refresh_tokens_are_unique_and_hashed_deterministically()
    {
        var service = CreateService(new FixedClock(DateTimeOffset.UtcNow));

        var first = service.CreateRefreshToken();
        var second = service.CreateRefreshToken();

        Assert.NotEqual(first.RawToken, second.RawToken);
        Assert.NotEqual(first.RawToken, first.TokenHash);
        Assert.Equal(first.TokenHash, service.HashRefreshToken(first.RawToken));
        // Hex-encoded SHA-256 — must match the fixed-length(64) column.
        Assert.Equal(64, first.TokenHash.Length);
    }

    [Fact]
    public void A_signing_key_shorter_than_32_bytes_is_refused_at_construction()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        // Failing loudly at startup beats silently issuing weakly-signed tokens.
        Assert.Throws<InvalidOperationException>(() => CreateService(clock, "too-short"));
    }
}

public class PasswordHasherTests
{
    private readonly PasswordHasherAdapter _hasher = new();

    [Fact]
    public void Hash_is_salted_so_the_same_password_hashes_differently_each_time()
    {
        var first = _hasher.Hash("CorrectHorse1");
        var second = _hasher.Hash("CorrectHorse1");

        Assert.NotEqual(first, second);
        Assert.Equal(PasswordVerification.Success, _hasher.Verify(first, "CorrectHorse1"));
        Assert.Equal(PasswordVerification.Success, _hasher.Verify(second, "CorrectHorse1"));
    }

    [Fact]
    public void Verification_fails_for_a_wrong_empty_or_malformed_value()
    {
        var hash = _hasher.Hash("CorrectHorse1");

        Assert.Equal(PasswordVerification.Failed, _hasher.Verify(hash, "wrong-password"));
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify(hash, ""));
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify("", "CorrectHorse1"));
        // A corrupted stored hash must read as a failure, not throw out of the login path.
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify("not-a-real-hash", "CorrectHorse1"));
    }
}

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Short1")]           // under the minimum length
    [InlineData("alllowercase1")]    // no uppercase
    [InlineData("ALLUPPERCASE1")]    // no lowercase
    [InlineData("NoDigitsAtAll")]    // no digit
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_weak_passwords(string password) =>
        Assert.NotNull(PasswordPolicy.Validate(password, minimumLength: 10));

    [Theory]
    [InlineData("CorrectHorse1")]
    [InlineData("Str0ngEnoughPassphrase")]
    public void Accepts_passwords_that_meet_the_policy(string password) =>
        Assert.Null(PasswordPolicy.Validate(password, minimumLength: 10));
}
