#nullable disable

using Microsoft.AspNetCore.Identity;

namespace Automaton.Studio.Server.Entities
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        public string Name { get; set; }
        public ICollection<ApplicationUserRole> UserRoles { get; set; }
    }
}
