using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Represents a project/organization that contains flows, apps, steps, and runners
/// </summary>
public class Project
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    public Guid AdminUserId { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    [Required]
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual ApplicationUser AdminUser { get; set; } = null!;
    public virtual ICollection<UserProjectRole> UserProjectRoles { get; set; } = new List<UserProjectRole>();
}
