using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Entity for storing AI model configuration for each provider (managed by system administrators)
/// This replaces the SupportedModels arrays in appsettings.json
/// </summary>
public class AiModel
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid ProviderId { get; set; } // Foreign key to AiProviderConfig

    [Required]
    [MaxLength(100)]
    public string ModelName { get; set; } = string.Empty; // Actual model name sent to API (e.g., "gpt-4o", "claude-3-5-sonnet-20241022")

    [Required]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty; // Friendly name for UI (e.g., "GPT-4 Omni", "Claude 3.5 Sonnet")

    [MaxLength(500)]
    public string? Description { get; set; } // Description of the model's capabilities

    [Required]
    public bool Enabled { get; set; } = true; // Whether this model is enabled

    public int DisplayOrder { get; set; } = 0; // For ordering in UI

    [Required]
    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Required]
    public Guid CreatedBy { get; set; } // User ID who created this config

    public Guid? UpdatedBy { get; set; } // User ID who last updated this config

    // Navigation property
    public virtual AiProvider Provider { get; set; } = default!;
}
