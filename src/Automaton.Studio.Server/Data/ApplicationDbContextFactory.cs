using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Automaton.Studio.Server.Data;

/// <summary>
/// Design-time factory for ApplicationDbContext to support EF Core migrations
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Create DbContext options using a dummy connection string for design-time only
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        
        // Use MySQL provider with a dummy connection string (won't actually connect)
        // This is only used for generating migrations
        optionsBuilder.UseMySql(
            "Server=localhost;Database=automaton_design;User=root;Password=root;",
            new MySqlServerVersion(new Version(8, 0, 21)),
            options => options.MigrationsAssembly("Automaton.Studio.Server")
        );

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
