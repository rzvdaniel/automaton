#nullable disable

using Automaton;
using Microsoft.AspNetCore.Identity;

namespace Automaton.Api.Entities;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUserRole : IdentityUserRole<Guid>
{
    public virtual ApplicationUser User { get; set; }
    public virtual ApplicationRole Role { get; set; }
}