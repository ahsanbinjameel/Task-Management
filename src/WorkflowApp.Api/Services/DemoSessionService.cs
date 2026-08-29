using System.Security.Claims;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Infrastructure.Identity;

namespace WorkflowApp.Api.Services;

/// <summary>
/// Reads demo mode off the caller's token.
///
/// It has to come from the token and nowhere else. A header would let any client put itself in demo
/// mode, and a server-side flag would be a second place the answer lives — able to disagree with the
/// signed one, which for a switch that decides <em>which database this request writes to</em> is the
/// worst possible kind of bug.
///
/// This is resolved once per request, before the DbContext is built, and its answer is what chooses
/// the connection string. See <c>AddInfrastructure</c>.
/// </summary>
public sealed class DemoSessionService : IDemoSession
{
    private readonly IHttpContextAccessor _accessor;

    public DemoSessionService(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsActive =>
        string.Equals(Principal?.FindFirstValue(AppClaimTypes.Demo), "true", StringComparison.Ordinal);

    public long? RealUserId =>
        long.TryParse(Principal?.FindFirstValue(AppClaimTypes.DemoRealUser), out var id) ? id : null;

    public string? RealUserName => Principal?.FindFirstValue(AppClaimTypes.DemoRealUserName);
}
