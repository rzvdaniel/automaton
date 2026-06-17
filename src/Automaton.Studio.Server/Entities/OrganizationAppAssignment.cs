using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>
/// Represents an app assignment to a user within an organization.
/// Allows AppUser role members to access specific apps.
/// </summary>
public class OrganizationAppAssignment
{
    [Key]
    public Guid Id { get; set; }
    
    /// <summary>
    /// The organization this assignment belongs to
    /// </summary>
    public Guid OrganizationId { get; set; }
    
    /// <summary>
    /// The app being assigned
    /// </summary>
    public Guid AppId { get; set; }
    
    /// <summary>
    /// The user this app is assigned to
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// Who made this assignment (usually an OrgAdmin)
    /// </summary>
    public Guid? AssignedBy { get; set; }
    
    /// <summary>
    /// When this assignment was created
    /// </summary>
    public DateTime AssignedAt { get; set; }
    
    /// <summary>
    /// Whether this assignment is active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public virtual Organization? Organization { get; set; }
    public virtual WebApp? App { get; set; }
    public virtual ApplicationUser? User { get; set; }
    public virtual ApplicationUser? Assigner { get; set; }
}
