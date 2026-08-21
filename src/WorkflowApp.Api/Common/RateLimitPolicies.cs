namespace WorkflowApp.Api.Common;

/// <summary>Named rate-limiting policies, referenced by <c>[EnableRateLimiting]</c>.</summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Applied to the unauthenticated credential endpoints. Account lockout already limits attempts
    /// against a single account; this limits the request volume a single client address can generate,
    /// which is what protects against spraying across many accounts.
    /// </summary>
    public const string Authentication = "authentication";
}
