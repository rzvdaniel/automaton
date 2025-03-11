using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Data
{
    public class MySqlDbContext(IConfiguration configuration) : ApplicationDbContext(configuration)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var connectionString = configuration.GetConnectionString("MySqlConnection") ?? 
                throw new ArgumentNullException("Connection string not found");

            options.UseMySQL(connectionString);
        }
    }
}
