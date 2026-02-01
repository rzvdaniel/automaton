using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Represents an organization that can contain multiple projects and manage team members.
/// Organizations enable enterprise features like app assignment to non-developer users.
/// </summary>
public class Organization
{
    [Key]
    public Guid Id { get; set; }
    
    /// <summary>
    /// Display name of the organization (e.g., "City Hospital")
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional description of the organization
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// The user who created/owns this organization
    /// </summary>
    public Guid OwnerId { get; set; }
    
    /// <summary>
    /// URL-friendly unique identifier (e.g., "city-hospital")
    /// </summary>
    [MaxLength(100)]
    public string? Slug { get; set; }
    
    // Settings
    /// <summary>
    /// Whether anyone can join this organization (requires email domain match if set)
    /// </summary>
    public bool AllowPublicSignup { get; set; } = false;
    
    /// <summary>
    /// If set, only users with this email domain can join (e.g., "@hospital.com")
    /// </summary>
    [MaxLength(255)]
    public string? RequireEmailDomain { get; set; }
    
    // Branding (future: SSO, billing)
    /// <summary>
    /// URL to organization logo
    /// </summary>
    [MaxLength(500)]
    public string? LogoUrl { get; set; }
    
    /// <summary>
    /// Primary brand color in hex format (e.g., "#1976D2")
    /// </summary>
    [MaxLength(7)]
    public string? PrimaryColor { get; set; }
    
    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Soft delete
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public virtual ApplicationUser? Owner { get; set; }
    public virtual ICollection<OrganizationMember> Members { get; set; } = new List<OrganizationMember>();
    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();
    public virtual ICollection<OrganizationAppAssignment> AppAssignments { get; set; } = new List<OrganizationAppAssignment>();
    public virtual ICollection<OrganizationInvitation> Invitations { get; set; } = new List<OrganizationInvitation>();
}
