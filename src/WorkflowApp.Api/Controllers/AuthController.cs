using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Identity.Dtos;
using WorkflowApp.Application.Identity.Services;

namespace WorkflowApp.Api.Controllers;

/// <summary>Authentication endpoints: login, refresh, logout, self-service password change.</summary>
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;

    private readonly IUserAdminService _users;

    public AuthController(IAuthService auth, IUserAdminService users)
    {
        _auth = auth;
        _users = users;
    }

    /// <summary>Exchanges credentials for an access token and a refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
        => FromResult(await _auth.LoginAsync(request, ct));

    /// <summary>Rotates a refresh token, returning a fresh access/refresh pair.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
        => FromResult(await _auth.RefreshAsync(request, ct));

    /// <summary>
    /// Revokes the supplied refresh token. Anonymous by design and idempotent: a client whose
    /// access token has already expired must still be able to clean up its session.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken ct)
        => FromResult(await _auth.LogoutAsync(request, ct));

    /// <summary>The caller's own profile, roles and effective permissions.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
        => FromResult(await _auth.GetCurrentUserAsync(CurrentUserId, ct));

    /// <summary>
    /// Changes the caller's own name or email. Not their username, roles or active state — those
    /// are an administrator's to set; see <c>IUserAdminService.UpdateProfileAsync</c>.
    /// </summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken ct)
        => FromResult(await _users.UpdateProfileAsync(CurrentUserId, request, ct));

    /// <summary>Changes the caller's own password. All their refresh tokens are revoked.</summary>
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
        => FromResult(await _auth.ChangePasswordAsync(CurrentUserId, request, ct));
    // --- acting as somebody else -----------------------------------------------------------------
    //
    // For demonstrating the product and for supporting somebody who cannot describe what they are
    // seeing. The session it issues carries the target's permissions and none of the caller's, so
    // it can only ever narrow what they can do, and every action taken while acting records both
    // people — see AuditLog.ImpersonatedByUserId.

    /// <summary>Who this administrator could act as.</summary>
    [HttpGet("impersonation-targets")]
    [HasPermission(Permissions.AdminImpersonate)]
    [ProducesResponseType(typeof(IReadOnlyList<ImpersonationTargetDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ImpersonationTargets(CancellationToken ct)
        => FromResult(await _auth.ImpersonationTargetsAsync(CurrentUserId, ct));

    /// <summary>Start acting as somebody else.</summary>
    [HttpPost("impersonate")]
    [HasPermission(Permissions.AdminImpersonate)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Impersonate(
        [FromBody] ImpersonateRequest request, CancellationToken ct)
        => FromResult(await _auth.ImpersonateAsync(CurrentUserId, request.UserId, ct));

    /// <summary>
    /// Stop acting and get your own session back.
    ///
    /// Deliberately *not* gated on the permission: the token in hand belongs to the person being
    /// acted as, who does not hold it. The real human is read from the token, so this can only ever
    /// return the caller to themselves.
    /// </summary>
    [HttpPost("stop-impersonating")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StopImpersonating(CancellationToken ct)
        => FromResult(await _auth.StopImpersonatingAsync(ct));

}
