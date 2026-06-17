using Automaton.Api.Enums;

namespace Automaton.Api.Entities;

/// <summary>
/// Represents a user's membership in a project with a specific role.
/// </summary>
public class ProjectMember
{
    /// <summary>
    /// The project this membership belongs to
    /// </summary>
    public Guid ProjectId { get; set; }
    
    /// <summary>
    /// The user who is a member
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// The role assigned to this user in this project
    /// </summary>
    public ProjectRole Role { get; set; }
    
    /// <summary>
    /// When this user joined the project
    /// </summary>
    public DateTime JoinedAt { get; set; }
    
    /// <summary>
    /// Who added this member to the project
    /// </summary>
    public Guid? InvitedBy { get; set; }
    
    /// <summary>
    /// Whether this membership is active (for soft delete)
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public virtual Project? Project { get; set; }
    public virtual ApplicationUser? User { get; set; }
    public virtual ApplicationUser? Inviter { get; set; }
}
