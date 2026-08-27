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
using WorkflowApp.Api.Hubs;
using WorkflowApp.Api.Middleware;
using WorkflowApp.Api.Services;
using WorkflowApp.Application;
using WorkflowApp.Application.Common.Events;
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

// Rendering, not business logic: it holds no state and takes no dependencies, so a singleton.
//
// PDFsharp resolves fonts through one process-wide hook rather than through DI, so it is set here
// and checked here. EnsureAvailable() throws at startup on a machine with no usable font, which is
// far better than the first person to open a report discovering it.
var fontResolver = new FileSystemFontResolver();
fontResolver.EnsureAvailable();
PdfSharp.Fonts.GlobalFontSettings.FontResolver = fontResolver;

builder.Services.AddSingleton<IDailyReportPdf, DailyReportPdf>();

// --- Authentication (JWT bearer) ----------------------------------------------------------
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing required configuration section 'Jwt'.");

// A placeholder signing key must never reach a deployed environment.
if (!builder.Environment.IsDevelopment() &&
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

    // A global ceiling per caller. Credential endpoints keep their own, much tighter, policy.
    // Partitioned by user where we know who is calling, by IP where we do not, so one noisy client
    // cannot spend everybody else's budget.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

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

// Replaces the no-op publisher registered by AddApplication. The hub lives in this layer, so the
// mapping from events to groups does too.
builder.Services.AddSingleton<IIntegrationEventPublisher, SignalRIntegrationEventPublisher>();

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
// Two switches, deliberately independent, because they carry very different risk.
//
// Applying migrations rewrites the schema. That is convenient in development and a hazard in
// production, where migrations belong in the deployment pipeline — so it is off by default outside
// Development.
//
// Seeding only inserts rows the application cannot function without: the permission catalog, the
// system roles and their grants, the pause reasons, the bootstrap administrator. It is idempotent
// and safe to repeat, so it runs everywhere by default. These used to be nested, which meant a
// production database came up with a perfectly good schema and no roles in it.
var applyMigrations = app.Configuration.GetValue(
    "Database:ApplyMigrationsOnStartup", app.Environment.IsDevelopment());
var seedOnStartup = app.Configuration.GetValue("Database:SeedOnStartup", true);

if (applyMigrations || seedOnStartup)
{
    using var scope = app.Services.CreateScope();

    if (applyMigrations)
    {
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        await db.Database.MigrateAsync();
    }

    if (seedOnStartup)
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

app.UseSecurityHeaders();
app.UseWorkflowExceptionHandling();

// Swagger is exposed in Development only, never in production.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// An internal host that serves plain HTTP on a LAN, or one sitting behind a proxy that already
// terminates TLS, would only get a broken hop from a redirect — hence the opt-out. It defaults to
// true, so a deployment has to say out loud that it does not want the redirect.
if (app.Configuration.GetValue("Security:RequireHttps", true))
{
    app.UseHttpsRedirection();
}

// The Angular client, built into wwwroot by `npm run build` in client/.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("client");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness only — deliberately no database call, so it still answers when SQL Server is down.
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// Readiness: can this instance actually serve traffic? Kept separate from liveness so a database
// outage takes the instance out of the load balancer without the host being killed and restarted.
app.MapGet("/health/ready", async (WorkflowDbContext db, CancellationToken ct) =>
{
    try
    {
        return await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "ready" })
            : Results.Json(new { status = "database unreachable" }, statusCode: 503);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "database error", detail = ex.Message }, statusCode: 503);
    }
}).AllowAnonymous().DisableRateLimiting();

// SPA fallback. Angular owns client-side routing, so a deep link like /tasks/5 is not a file on
// disk — without this, refreshing anywhere but the root 404s. Registered after MapControllers and
// the health endpoints so it only catches what nothing else claimed, and it deliberately excludes
// the API and hub prefixes: an unknown /api path must still return 404, not an HTML page that a
// fetch() would try to parse as JSON.
app.MapFallbackToFile("index.html").Add(builder =>
{
    var original = builder.RequestDelegate!;
    builder.RequestDelegate = context =>
    {
        var path = context.Request.Path;

        if (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs") ||
            path.StartsWithSegments("/swagger") || path.StartsWithSegments("/health"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return original(context);
    };
});

app.MapHub<WorkflowHub>("/hubs/workflow");

app.Run();
