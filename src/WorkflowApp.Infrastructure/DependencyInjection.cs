using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Infrastructure.Common;
using WorkflowApp.Infrastructure.Identity;
using WorkflowApp.Infrastructure.Persistence;
using WorkflowApp.Infrastructure.Persistence.Interceptors;
using WorkflowApp.Infrastructure.Persistence.Seed;
using WorkflowApp.Infrastructure.Storage;

namespace WorkflowApp.Infrastructure;

/// <summary>Which store the application is running against.</summary>
public enum DatabaseProvider
{
    /// <summary>The deployment target and the source of truth.</summary>
    SqlServer = 0,

    /// <summary>
    /// Local demo/dev only: a single file, no server to install. Schema is created with
    /// <c>EnsureCreated</c> from the model rather than from migrations, because the migrations are
    /// authored for SQL Server. Never use this for anything that matters.
    /// </summary>
    Sqlite = 1
}

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

        var provider = configuration.GetValue("Database:Provider", DatabaseProvider.SqlServer);
        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<WorkflowDbContext>((sp, options) =>
        {
            switch (provider)
            {
                case DatabaseProvider.Sqlite:
                    options.UseSqlite(connectionString ?? "Data Source=workflowapp-demo.db");
                    break;

                default:
                    options.UseSqlServer(
                        connectionString,
                        sql => sql.MigrationsAssembly(typeof(WorkflowDbContext).Assembly.FullName));
                    break;
            }

            options.AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>());
        });

        // The Application layer only ever sees the interface.
        services.AddScoped<IWorkflowDbContext>(sp => sp.GetRequiredService<WorkflowDbContext>());

        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IFileStorage, DiskFileStorage>();

        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<DemoDataSeeder>();

        return services;
    }

    public static DatabaseProvider GetDatabaseProvider(this IConfiguration configuration) =>
        configuration.GetValue("Database:Provider", DatabaseProvider.SqlServer);
}
