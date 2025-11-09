using Automaton.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Join table defining user access to projects with specific roles
/// </summary>
public class UserProjectRole
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public Guid ProjectId { get; set; }

    [Required]
    public ProjectRole Role { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Project Project { get; set; } = null!;
}
