using Automaton.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

public class ApiKey
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string HashedKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string KeyPrefix { get; set; } = string.Empty; // For display purposes (e.g., "ak_runner_1234...")

    [Required]
    public Guid UserId { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;

    // Set default to Runner to match database configuration
    public ApiKeyScope Scope { get; set; } = ApiKeyScope.Runner;

    [MaxLength(500)]
    public string? Description { get; set; }

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
}