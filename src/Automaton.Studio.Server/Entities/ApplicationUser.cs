#nullable disable

using Microsoft.AspNetCore.Identity;

namespace Automaton.Studio.Server.Entities
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        public string Name { get; set; }
        public ICollection<ApplicationUserRole> UserRoles { get; set; }

        // Public profile fields
        /// <summary>
        /// Display name shown on public profile (defaults to Name if not set)
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Public-facing username for shareable URLs (privacy-safe, no email)
        /// Required, unique, URL-safe (alphanumeric, hyphens, underscores), 3-32 characters
        /// </summary>
        public required string PublicUsername { get; set; }

        /// <summary>
        /// User bio/description for public profile
        /// </summary>
        public string Bio { get; set; }

        /// <summary>
        /// URL to user's avatar image
        /// </summary>
        public string AvatarUrl { get; set; }

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
        
        // Navigation properties for organization relationships
        public virtual ICollection<Organization> OwnedOrganizations { get; set; }
        public virtual ICollection<OrganizationMember> OrganizationMemberships { get; set; }
    }
}
