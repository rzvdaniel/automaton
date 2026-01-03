using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Entity for storing user's AI API keys (OpenAI, Anthropic, Google, etc.)
/// These are separate from Automaton's own API keys used for runner authentication
/// </summary>
public class AiApiKey
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty; // User-friendly name like "My OpenAI Key"

    /// <summary>
    /// Provider name string matching AiProvider.Name (e.g., "OpenAI", "Anthropic", "Google")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Optional foreign key to AiProvider for referential integrity
    /// </summary>
    public Guid? AiProviderId { get; set; }

    [ForeignKey(nameof(AiProviderId))]
    public virtual AiProvider? AiProvider { get; set; }

    [Required]
    [MaxLength(1000)]
    public string EncryptedApiKey { get; set; } = string.Empty; // Encrypted storage

    [Required]
    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? Description { get; set; }

    // Navigation properties for many-to-many relationship with users and projects
    public virtual ICollection<AiApiKeyUser> AiApiKeyUsers { get; set; } = new List<AiApiKeyUser>();
    public virtual ICollection<ProjectAiApiKey> ProjectAiApiKeys { get; set; } = new List<ProjectAiApiKey>();
}