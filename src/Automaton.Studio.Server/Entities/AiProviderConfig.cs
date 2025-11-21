using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Entity for storing AI provider configuration (managed by system administrators)
/// This replaces the appsettings.json AiProviders configuration
/// </summary>
public class AiProviderConfig
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string ProviderName { get; set; } = string.Empty; // e.g., "OpenAI", "Anthropic", "Google"

    [Required]
    public AiProvider Provider { get; set; } // Enum value

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(100)]
    public string DefaultModel { get; set; } = string.Empty; // Default model for this provider

    [Required]
    public bool Enabled { get; set; } = true; // Whether this provider is enabled

    public int DisplayOrder { get; set; } = 0; // For ordering in UI

    [Required]
    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Required]
    public Guid CreatedBy { get; set; } // User ID who created this config

    public Guid? UpdatedBy { get; set; } // User ID who last updated this config

    // Navigation properties
    public virtual ICollection<AiModelConfig> Models { get; set; } = new List<AiModelConfig>();
}
