using System.ComponentModel.DataAnnotations;
using Automaton.Api.Enums;

namespace Automaton.Api.Entities;

/// <summary>
/// Represents a user's membership in an organization with a specific role.
/// </summary>
public class OrganizationMember
{
    [Key]
    public Guid Id { get; set; }
    
    /// <summary>
    /// The organization this membership belongs to
    /// </summary>
    public Guid OrganizationId { get; set; }
    
    /// <summary>
    /// The user who is a member
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// The role assigned to this user in this organization
    /// </summary>
    public OrganizationRole Role { get; set; }
    
    /// <summary>
    /// Who added this member to the organization
    /// </summary>
    public Guid? InvitedBy { get; set; }
    
    /// <summary>
    /// When this user joined the organization
    /// </summary>
    public DateTime JoinedAt { get; set; }
    
    /// <summary>
    /// Whether this membership is active (for soft delete)
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public virtual Organization? Organization { get; set; }
    public virtual ApplicationUser? User { get; set; }
    public virtual ApplicationUser? Inviter { get; set; }
}
