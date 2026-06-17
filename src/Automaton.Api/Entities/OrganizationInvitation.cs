using System.ComponentModel.DataAnnotations;
using Automaton.Api.Enums;

namespace Automaton.Api.Entities;

/// <summary>
/// Represents an invitation token for a user to join an organization.
/// </summary>
public class OrganizationInvitation
{
    [Key]
    public Guid Id { get; set; }
    
    /// <summary>
    /// The organization the user is being invited to
    /// </summary>
    public Guid OrganizationId { get; set; }
    
    /// <summary>
    /// Email address of the invited user
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;
    
    /// <summary>
    /// The role the user will have when they accept the invitation
    /// </summary>
    public OrganizationRole Role { get; set; }
    
    /// <summary>
    /// Secure random token for accepting the invitation
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string Token { get; set; } = string.Empty;
    
    /// <summary>
    /// Who sent this invitation
    /// </summary>
    public Guid InvitedBy { get; set; }
    
    /// <summary>
    /// When this invitation was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// When this invitation expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }
    
    /// <summary>
    /// When this invitation was accepted (null if pending)
    /// </summary>
    public DateTime? AcceptedAt { get; set; }
    
    /// <summary>
    /// Whether this invitation has been consumed
    /// </summary>
    public bool IsConsumed { get; set; } = false;
    
    // Navigation properties
    public virtual Organization? Organization { get; set; }
    public virtual ApplicationUser? Inviter { get; set; }
}
