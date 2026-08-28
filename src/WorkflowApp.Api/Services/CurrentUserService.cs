using System.Security.Claims;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Infrastructure.Identity;

namespace WorkflowApp.Api.Services;

/// <summary>
/// Projects the authenticated principal (and request metadata) onto the Application layer's
/// <see cref="ICurrentUser"/>, so services never touch <c>HttpContext</c>.
/// </summary>
public sealed class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public long? UserId =>
        long.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public IReadOnlySet<string> Permissions =>
        Principal?.FindAll(AppClaimTypes.Permission).Select(c => c.Value).ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The real human when an administrator is acting as somebody else. Read from the token like
    /// everything else about the caller, so there is one answer rather than two.
    /// </summary>
    public long? ImpersonatedByUserId =>
        long.TryParse(Principal?.FindFirstValue(AppClaimTypes.ImpersonatedBy), out var id) ? id : null;

    public string? ImpersonatedByUserName => Principal?.FindFirstValue(AppClaimTypes.ImpersonatedByName);

    /// <summary>
    /// Behind IIS or a reverse proxy this is only correct once forwarded headers are configured —
    /// see the deployment runbook. Used for the audit trail, never for authorization.
    /// </summary>
    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var raw = _accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            // The column is nvarchar(512); clients can send far more than that.
            return raw.Length > 512 ? raw[..512] : raw;
        }
    }
}
