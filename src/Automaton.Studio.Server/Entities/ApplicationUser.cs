#nullable disable

using Microsoft.AspNetCore.Identity;

namespace Automaton.Studio.Server.Entities
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        public string Name { get; set; }
        public ICollection<ApplicationUserRole> UserRoles { get; set; }
        
        // Project/Organization features
        /// <summary>
        /// Whether this user has access to enterprise features (Projects)
        /// </summary>
        public bool IsEnterpriseEnabled { get; set; } = false;
        
        /// <summary>
        /// The currently selected project for this user's session (optional UX enhancement)
        /// </summary>
        public Guid? CurrentProjectId { get; set; }
        
        // Navigation properties for project relationships
        public virtual ICollection<Project> OwnedProjects { get; set; }
        public virtual ICollection<ProjectMember> ProjectMemberships { get; set; }
    }
}
