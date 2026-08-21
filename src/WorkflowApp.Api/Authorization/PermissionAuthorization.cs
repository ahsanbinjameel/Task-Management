using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using WorkflowApp.Infrastructure.Identity;

namespace WorkflowApp.Api.Authorization;

/// <summary>Requires a single permission key to be present on the caller's identity.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission) => Permission = permission;

    public string Permission { get; }
}

/// <summary>
/// Succeeds when the access token carries a matching <c>permission</c> claim. Permissions are
/// embedded at token-issue time, so this costs no database round trip.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var granted = context.User.Claims.Any(c =>
            c.Type == AppClaimTypes.Permission &&
            string.Equals(c.Value, requirement.Permission, StringComparison.Ordinal));

        if (granted)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Materialises a policy per permission key on demand, so adding a permission to the catalog never
/// requires touching startup registration. Policies are named <c>perm:{key}</c>; anything else
/// falls through to the default provider.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "perm:";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) =>
        _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            return _fallback.GetPolicyAsync(policyName);

        var permission = policyName[PolicyPrefix.Length..];

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

/// <summary>
/// Declarative permission gate: <c>[HasPermission(Permissions.AdminManageUsers)]</c>.
/// This is the security boundary — hiding UI elements is convenience only.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission)
        : base(PermissionPolicyProvider.PolicyPrefix + permission) { }
}
