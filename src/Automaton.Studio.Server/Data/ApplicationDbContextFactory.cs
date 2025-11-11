using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Automaton.Studio.Server.Data;

/// <summary>
/// Design-time factory for ApplicationDbContext to enable EF Core migrations
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        
        // Use MySQL for migrations (default configuration)
        var connectionString = "Server=localhost;Database=automaton;User=root;Password=root;";
        optionsBuilder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)));
        
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}

