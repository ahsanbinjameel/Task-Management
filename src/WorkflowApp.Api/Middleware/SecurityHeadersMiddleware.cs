namespace WorkflowApp.Api.Middleware;

/// <summary>
/// Response headers that harden the browser side of the application.
///
/// Most of this API is consumed by a client that never renders its responses, so these headers earn
/// their place mainly because of what the host also serves: the Angular client, Swagger, and any
/// error page. A JSON endpoint that a browser can be tricked into interpreting as HTML is the
/// classic route to a stored XSS, and <c>X-Content-Type-Options</c> is what closes it.
///
/// Note what is deliberately <b>not</b> here: CSRF tokens. Authentication is a bearer token in the
/// Authorization header, never a cookie, so a cross-site request cannot carry the caller's
/// credentials in the first place. Adding tokens would be ceremony against a threat the auth design
/// already rules out. That reasoning stops holding the day anything moves to cookie auth.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _allowSwaggerInlineScripts;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
    {
        _next = next;

        // Swagger UI is inline script and style. It is only served locally, so the relaxation is
        // confined to the environments that actually serve it.
        _allowSwaggerInlineScripts = environment.IsDevelopment();
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // No MIME sniffing: a JSON response must never be executed as script.
        headers["X-Content-Type-Options"] = "nosniff";

        // No framing: this application is never embedded, so clickjacking has no surface.
        headers["X-Frame-Options"] = "DENY";

        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Nothing here needs a camera, microphone or location.
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

        var scriptSrc = _allowSwaggerInlineScripts
            ? "script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'"
            : "script-src 'self'; style-src 'self'";

        // blob: is on img-src for the attachment thumbnails and the image viewer, and on frame-src
        // for the PDF viewer. Both exist for the same reason: the bytes are fetched with the
        // caller's bearer token — an <img src> or an <iframe src> cannot carry one — so our own
        // script turns the response into a blob URL. Those URLs are same-origin, unguessable and
        // last only as long as the page: they cannot pull in anything from anywhere else.
        //
        // frame-src has to be named explicitly. Without it the browser falls back to default-src,
        // which is 'self' only, and the PDF viewer renders an empty frame with a console error.
        // Note this is unrelated to frame-ancestors/X-Frame-Options below: those govern who may
        // frame *us*, and stay closed.
        headers["Content-Security-Policy"] =
            $"default-src 'self'; {scriptSrc}; img-src 'self' data: blob:; " +
            "frame-src 'self' blob:; object-src 'self' blob:; " +
            // The SignalR WebSocket connects back to this origin and nowhere else.
            "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

        return _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
