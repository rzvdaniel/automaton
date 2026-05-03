using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Database entity representing an AI provider (OpenAI, Anthropic, Google, etc.)
/// Replaces hardcoded configuration in appsettings.json
/// </summary>
public class AiProvider
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty; // "OpenAI", "Anthropic", "Google"

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty; // "OpenAI (ChatGPT)", "Anthropic (Claude)"

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(200)]
    public string? BaseUrl { get; set; } // For custom endpoints (e.g., Azure OpenAI)

    public bool IsActive { get; set; } = true;

    public bool RequiresApiKey { get; set; } = true;

    [MaxLength(100)]
    public string? EnvironmentKeyName { get; set; } // "OPENAI_API_KEY", "ANTHROPIC_API_KEY"

    [MaxLength(500)]
    public string? ApiKeyHelpUrl { get; set; } // "https://platform.openai.com/api-keys"

    [MaxLength(50)]
    public string? IconName { get; set; } // "openai", "anthropic", "google" for UI icon

    /// <summary>
    /// JSON object merged into every request body for all models of this provider.
    /// Use for provider-wide parameters (e.g. OpenRouter routing hints).
    /// Model-level ExtraBodyJson is merged on top, with model values taking precedence.
    /// </summary>
    [MaxLength(2000)]
    public string? ExtraBodyJson { get; set; }

    public int SortOrder { get; set; } = 0;

    [Required]
    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual ICollection<AiModel> Models { get; set; } = new List<AiModel>();
}
