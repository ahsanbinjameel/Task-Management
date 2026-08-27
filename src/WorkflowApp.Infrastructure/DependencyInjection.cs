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
        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<WorkflowDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                connectionString,
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

        return services;
    }
}
