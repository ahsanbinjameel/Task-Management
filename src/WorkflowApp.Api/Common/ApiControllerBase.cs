using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Models;
using WorkflowApp.Application.Common.Services;
using WorkflowApp.Infrastructure.Identity;

namespace WorkflowApp.Api.Common;

/// <summary>
/// Shared plumbing for API controllers: the single place where an Application-layer
/// <see cref="Result"/> becomes an HTTP response, so error shapes stay consistent.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The authenticated user's id. Only call this on endpoints that require auth.</summary>
    protected long CurrentUserId =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("No authenticated user on this request.");

    /// <summary>
    /// Today's date in the configured business time zone — the default for date-scoped queries.
    /// Resolved from the request container so controllers don't each have to take the dependency.
    /// </summary>
    protected DateOnly Today
    {
        get
        {
            var services = HttpContext.RequestServices;
            var calendar = services.GetRequiredService<IBusinessCalendar>();
            var clock = services.GetRequiredService<IDateTimeProvider>();
            return calendar.ToBusinessDate(clock.UtcNow);
        }
    }

    /// <summary>
    /// How much of the workflow this caller is shown. Derived from their permissions, so it needs
    /// no extra plumbing on the client and cannot be asked for by someone it does not belong to.
    /// </summary>
    protected StatusAudience Audience => StatusViews.AudienceFor(CurrentPermissions);

    /// <summary>
    /// The caller's effective permissions, straight off the token. For the handful of services
    /// that shape a whole response around what someone is allowed to do rather than simply being
    /// gated on one permission.
    /// </summary>
    protected IReadOnlySet<string> CurrentPermissions =>
        User.Claims.Where(c => c.Type == AppClaimTypes.Permission)
            .Select(c => c.Value)
            .ToHashSet();

    /// <summary>
    /// Whether the caller holds a permission. Used where the check is conditional rather than a
    /// blanket gate on the action — for example, scoping a list to the caller's own rows.
    /// </summary>
    protected bool HasPermission(string permission) =>
        User.Claims.Any(c => c.Type == AppClaimTypes.Permission && c.Value == permission);

    protected IActionResult FromResult(Result result) =>
        result.IsSuccess ? NoContent() : Problem(result.Error!);

    protected IActionResult FromResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Problem(result.Error!);

    protected IActionResult Problem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = error.Message,
            Type = $"https://workflowapp/errors/{error.Code}",
            Instance = HttpContext.Request.Path
        };

        // Machine-readable code so clients branch on a stable value, not on prose.
        problem.Extensions["code"] = error.Code;

        return StatusCode(status, problem);
    }
}
