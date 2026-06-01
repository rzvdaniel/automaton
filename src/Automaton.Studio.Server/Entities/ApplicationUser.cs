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

        // Stripe billing
        /// <summary>Stripe Customer ID (cus_xxx). Created on first checkout.</summary>
        public string StripeCustomerId { get; set; }

        /// <summary>Active Stripe Subscription ID (sub_xxx). Null on Free plan.</summary>
        public string StripeSubscriptionId { get; set; }

        /// <summary>Current Automaton subscription plan. Null means no paid plan is assigned.</summary>
        public Guid? SubscriptionPlanId { get; set; }

        /// <summary>
        /// Grants access to internal hosted-runner plans whose catalog visibility is test-users-only.
        /// </summary>
        public bool CanAccessHostedRunnerTestPlans { get; set; }

        /// <summary>
        /// UTC timestamp when the account was first registered.
        /// </summary>
        public DateTime RegisteredAtUtc { get; set; }

        /// <summary>
        /// UTC timestamp of the most recent successful interactive login.
        /// </summary>
        public DateTime? LastLoginAtUtc { get; set; }

        /// <summary>
        /// Source used to create the account (password, github, invitation, adminCreated).
        /// </summary>
        public string RegistrationProvider { get; set; }

        /// <summary>
        /// Soft-delete flag used for admin cleanup without destroying audit relationships.
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// UTC timestamp when the account was soft deleted.
        /// </summary>
        public DateTime? DeletedAtUtc { get; set; }

        /// <summary>
        /// Admin user who soft deleted the account.
        /// </summary>
        public Guid? DeletedByUserId { get; set; }

        public virtual SubscriptionPlan SubscriptionPlan { get; set; }
    }
}
