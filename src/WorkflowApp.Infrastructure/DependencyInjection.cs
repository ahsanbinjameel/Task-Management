using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Application.Demo;
using WorkflowApp.Infrastructure.Common;
using WorkflowApp.Infrastructure.Demo;
using WorkflowApp.Infrastructure.Identity;
using WorkflowApp.Infrastructure.Persistence;
using WorkflowApp.Infrastructure.Persistence.Interceptors;
using WorkflowApp.Infrastructure.Persistence.Seed;
using WorkflowApp.Infrastructure.Storage;

namespace WorkflowApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<WorkforceOptions>(configuration.GetSection(WorkforceOptions.SectionName));
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        // Scoped: the interceptor reads the per-request ICurrentUser.
        services.AddScoped<AuditableEntityInterceptor>();

        // Scoped too: it drains the per-request integration-event queue after the save commits.
        services.AddScoped<IntegrationEventDispatchInterceptor>();

        // SQL Server is the only store, in every environment. There is deliberately no
        // second provider to run against: a lighter one would need its own schema shim, and
        // anything it did not enforce — ROWVERSION above all — would be a rule the code believes
        // it has and the database does not.
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        // Demo mode is the same schema in a different catalog on the same server — same provider,
        // same migrations, same everything the code believes about the database. That is what
        // makes it a demonstration of the product rather than of a lookalike, and it is the
        // difference between this and the SQLite demo profile that was removed (CLAUDE.md §6).
        // Derived from Default by suffixing the catalog, unless a site sets one explicitly.
        //
        // Derivation is what makes this work unchanged on a second machine, and getting it wrong is
        // not theoretical: a tracked `"Demo": "Server=localhost;..."` was tried first and broke on
        // the machine whose user-secrets point Default at (localdb)\MSSQLLocalDB — live worked and
        // demo could not find a server. Following whatever Default actually resolves to is the only
        // form with no second thing to configure and keep true.
        var demoConnectionString = configuration.GetConnectionString("Demo")
            ?? DeriveDemoConnection(connectionString);

        // The one check that must never be skipped. If these ever resolve to the same catalog then
        // "demo" writes are live writes, the isolation the feature promises is a fiction, and
        // Reset Demo would erase real work. Refuse at startup rather than find out later.
        if (demoConnectionString is not null && PointsAtSameDatabase(connectionString, demoConnectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Demo points at the same database as Default. Demo mode must be "
                + "a separate catalog — otherwise demonstrations write to live data and Reset Demo "
                + "would destroy it.");
        }

        services.AddDbContext<WorkflowDbContext>((sp, options) =>
        {
            // Resolved per scope, so it is per request: the same DbContext type, pointed at
            // whichever catalog this caller's token says they are in. Every service above it is
            // unchanged and unaware, which is exactly the property being bought.
            var demo = sp.GetRequiredService<IDemoSession>();

            var target = demo.IsActive && demoConnectionString is not null
                ? demoConnectionString
                : connectionString;

            options.UseSqlServer(
                target,
                sql => sql.MigrationsAssembly(typeof(WorkflowDbContext).Assembly.FullName));

            options.AddInterceptors(
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<IntegrationEventDispatchInterceptor>());
        });

        // The Application layer only ever sees the interface.
        services.AddScoped<IWorkflowDbContext>(sp => sp.GetRequiredService<WorkflowDbContext>());

        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IFileStorage, DiskFileStorage>();

        services.AddScoped<DatabaseSeeder>();

        // The demo catalog. Scoped because it opens short-lived contexts of its own; see the class
        // note for why it cannot use the request-scoped one.
        services.AddScoped<IDemoEnvironment, DemoEnvironment>();

        return services;
    }

    /// <summary>
    /// The demo catalog beside the live one: same server, same credentials, <c>_Demo</c> appended.
    ///
    /// Deriving it rather than demanding a second setting is what lets the application come up on a
    /// machine nobody has configured for demo mode. Returns null when the string has no catalog to
    /// suffix, and demo mode is then simply unavailable rather than pointed somewhere unintended.
    /// </summary>
    private static string? DeriveDemoConnection(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.InitialCatalog)) return null;

            builder.InitialCatalog += "_Demo";
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether two connection strings name the same database on the same server.
    ///
    /// Compared on the parsed parts rather than the text, because the two are written by different
    /// people at different times and <c>Server=localhost</c> and <c>Server=LOCALHOST</c> are the
    /// same machine however differently they are typed.
    /// </summary>
    private static bool PointsAtSameDatabase(string first, string second)
    {
        try
        {
            var a = new SqlConnectionStringBuilder(first);
            var b = new SqlConnectionStringBuilder(second);

            return string.Equals(a.DataSource, b.DataSource, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.InitialCatalog, b.InitialCatalog, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // An unparseable string is somebody else's error to report — the DbContext will fail
            // with a better message than anything that could be raised from here.
            return false;
        }
    }
}
