using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkflowApp.Domain.Workflow;

namespace WorkflowApp.Api.Middleware;

/// <summary>
/// Turns unhandled exceptions into RFC 7807 <see cref="ProblemDetails"/>. Two categories get
/// meaningful statuses rather than a blanket 500:
/// workflow violations (400) and optimistic-concurrency conflicts (409).
///
/// Exception detail is never sent to the client outside Development — it goes to the log.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (status, title, code) = exception switch
        {
            InvalidWorkflowTransitionException => (
                StatusCodes.Status400BadRequest,
                exception.Message,
                "workflow.transition_not_allowed"),

            TransitionReasonRequiredException => (
                StatusCodes.Status400BadRequest,
                exception.Message,
                "workflow.reason_required"),

            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "This record was modified by someone else. Reload and try again.",
                "concurrency.conflict"),

            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                "server.unexpected")
        };

        if (status >= StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            _logger.LogWarning(exception, "Request rejected on {Method} {Path}: {Code}", context.Request.Method, context.Request.Path, code);

        // Something already started writing the response — the client will see a truncated body,
        // and there is nothing useful left to add.
        if (context.Response.HasStarted) return;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://workflowapp/errors/{code}",
            Instance = context.Request.Path
        };

        problem.Extensions["code"] = code;

        if (_environment.IsDevelopment() && status >= StatusCodes.Status500InternalServerError)
            problem.Detail = exception.ToString();

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseWorkflowExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionHandlingMiddleware>();
}
