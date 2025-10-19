using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Automaton.Studio.Server.Data;

public class MySqlDbContextFactory : IDesignTimeDbContextFactory<MySqlDbContext>
{
    public MySqlDbContext CreateDbContext(string[] args)
    {
        // Create a configuration with a placeholder connection string for design-time
        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true);
        
        // Add in-memory configuration for design-time if connection string is missing
        var tempConfig = configurationBuilder.Build();
        var connectionString = tempConfig.GetConnectionString("MySqlConnection");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            // Provide a minimal connection string for migration generation only
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string>
            {
                ["ConnectionStrings:MySqlConnection"] = "Server=localhost;Database=automaton;User=root;Password=password;"
            });
        }
        
        var configuration = configurationBuilder.Build();
        return new MySqlDbContext(configuration);
    }
}
