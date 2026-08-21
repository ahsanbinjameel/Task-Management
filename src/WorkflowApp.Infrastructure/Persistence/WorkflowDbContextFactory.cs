using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace WorkflowApp.Infrastructure.Persistence;

/// <summary>
/// Used only by the <c>dotnet ef</c> tooling. Having it means migrations can be created and
/// scripted without booting the API host — and, importantly, without a reachable SQL Server:
/// scaffolding a migration reads the model, not the database.
///
/// Runtime configuration still comes from the API's DI container, not from here.
/// </summary>
public sealed class WorkflowDbContextFactory : IDesignTimeDbContextFactory<WorkflowDbContext>
{
    /// <summary>
    /// Only ever used to satisfy the provider at scaffold time when no configuration file is found.
    /// It is never connected to during <c>migrations add</c>.
    /// </summary>
    private const string DesignTimeFallbackConnection =
        "Server=(localdb)\\MSSQLLocalDB;Database=WorkflowApp_Design;Trusted_Connection=True;TrustServerCertificate=True";

    public WorkflowDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default") ?? DesignTimeFallbackConnection;

        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(WorkflowDbContext).Assembly.FullName))
            .Options;

        return new WorkflowDbContext(options);
    }
}
