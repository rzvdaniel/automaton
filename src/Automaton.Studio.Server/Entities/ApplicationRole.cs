#nullable disable

using Automaton;
using Microsoft.AspNetCore.Identity;

namespace Automaton.Api.Entities
{
    public class ApplicationRole : IdentityRole<Guid>
    {
        public ICollection<ApplicationUserRole> UserRoles { get; set; }
    }
}