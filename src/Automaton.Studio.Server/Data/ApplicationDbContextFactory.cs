using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Automaton.Studio.Server.Data;

/// <summary>
/// Design-time factory for creating ApplicationDbContext instances for EF Core migrations.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        
        // Use a connection string for design-time operations
        // This is only used during migration generation, not at runtime
        optionsBuilder.UseMySql(
            "Server=localhost;Database=automaton;User=root;Password=password;",
            new MySqlServerVersion(new Version(8, 0, 36))
        );

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
