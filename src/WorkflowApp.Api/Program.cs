using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json.Serialization;
using WorkflowApp.Api.Authorization;
using WorkflowApp.Api.Common;
using WorkflowApp.Api.Middleware;
using WorkflowApp.Api.Services;
using WorkflowApp.Application;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Infrastructure;
using WorkflowApp.Infrastructure.Identity;
using WorkflowApp.Infrastructure.Persistence;
using WorkflowApp.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

// --- Layers -------------------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// The Application layer's view of "who is calling", sourced from the JWT.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();

// --- Authentication (JWT bearer) ----------------------------------------------------------
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing required configuration section 'Jwt'.");

// A placeholder signing key must never reach a deployed environment.
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Demo") &&
    (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || jwtOptions.SigningKey.StartsWith("REPLACE_WITH")))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is unset or still the placeholder. Provide a real key via environment " +
        "variable (Jwt__SigningKey) or user-secrets before running outside Development.");
}

// We issue ClaimTypes.* directly, so the handler's legacy inbound remapping would only create
// duplicate claims. Clearing it keeps what arrives identical to what was issued.
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            // No grace period: access tokens are short-lived and refresh is cheap.
            ClockSkew = TimeSpan.Zero,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };

        // Phase 9: SignalR sends the token in the query string because browsers cannot set
        // headers on a WebSocket handshake.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

// --- Authorization (permission-based) -----------------------------------------------------
// Policies are created on demand from the permission key, so the catalog can grow without
// touching startup.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization();

// --- Rate limiting ------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitPolicies.Authentication, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// --- Background work ----------------------------------------------------------------------
// Closes shifts abandoned without an explicit End Shift; see StaleShiftSweepService.
builder.Services.AddHostedService<StaleShiftSweepService>();

// --- Real-time (SignalR) ------------------------------------------------------------------
builder.Services.AddSignalR();

// --- API surface --------------------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums travel as names, not ordinals. "InProgress" survives a reordered enum and is
        // readable in logs and on the wire; a bare 20 is neither.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "WorkflowApp API", Version = "v1" });
    options.UseAllOfToExtendReferenceSchemas();

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token returned by /api/auth/login.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

// CORS for the Angular client. Credentials are allowed, so origins must be explicit.
builder.Services.AddCors(o => o.AddPolicy("client", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>())
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// --- Database bring-up --------------------------------------------------------------------
// Off by default for SQL Server: applying migrations automatically is convenient in development
// and a hazard in production, where migrations belong in the deployment pipeline.
if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

    if (app.Configuration.GetDatabaseProvider() == DatabaseProvider.Sqlite)
    {
        // The migrations are authored for SQL Server, so the demo store is built straight from
        // the model instead. Same schema shape, no migration history.
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }

    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();

    // Sample people, requests and tasks. Local evaluation only — see DemoDataSeeder.
    if (app.Configuration.GetValue("Database:SeedDemoData", false))
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
}

app.UseWorkflowExceptionHandling();

// Swagger is exposed in Development and in the local Demo environment, never in production.
var isLocal = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Demo");

if (isLocal)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// The Demo profile is plain HTTP on localhost; redirecting there would only produce a broken
// hop to a port that is not listening.
if (!app.Environment.IsEnvironment("Demo"))
    app.UseHttpsRedirection();

// The dev console (wwwroot/index.html) — a plain HTML client for exercising the API until the
// Angular front end exists.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("client");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness only — deliberately no database call, so it still answers when SQL Server is down.
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// TODO Phase 9: app.MapHub<WorkflowHub>("/hubs/workflow");

app.Run();
