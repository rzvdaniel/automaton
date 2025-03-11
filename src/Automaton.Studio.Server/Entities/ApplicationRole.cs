#nullable disable

using Microsoft.AspNetCore.Identity;

namespace Automaton.Studio.Server.Entities
{
    public class ApplicationRole : IdentityRole<Guid>
    {
        public ICollection<ApplicationUserRole> UserRoles { get; set; }
    }
}