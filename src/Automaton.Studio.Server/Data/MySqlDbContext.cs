using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Data
{
    public class MySqlDbContext : ApplicationDbContext
    {
        public MySqlDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    }
}
