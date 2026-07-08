using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace Automaton.Api.Data;

/// <summary>
/// Design-time factory for creating ApplicationDbContext instances for EF Core migrations.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var startupProjectPath = ResolveStartupProjectPath();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(startupProjectPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("MySqlConnection")
            ?? throw new InvalidOperationException("Connection string 'MySqlConnection' not found.");
        var serverVersionValue = configuration["DatabaseServerVersion"] ?? "8.0.36-mysql";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseMySql(
            connectionString,
            ServerVersion.Parse(serverVersionValue),
            mySqlOptions => mySqlOptions.MigrationsAssembly(Assembly.GetExecutingAssembly().GetName().Name));

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    private static string ResolveStartupProjectPath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var apiProjectPath = Path.GetFullPath(Path.Combine(currentDirectory, "..", "Automaton.Api"));

        return Directory.Exists(apiProjectPath)
            ? apiProjectPath
            : currentDirectory;
    }
}
