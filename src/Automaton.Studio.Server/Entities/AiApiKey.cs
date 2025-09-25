using System.ComponentModel.DataAnnotations;

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

    [Required]
    public AiProvider Provider { get; set; }

    [Required]
    [MaxLength(1000)]
    public string EncryptedApiKey { get; set; } = string.Empty; // Encrypted storage

    [Required]
    public Guid UserId { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? Description { get; set; }

    public virtual ApplicationUser User { get; set; } = null!;
}

public enum AiProvider
{
    OpenAI = 1,
    Anthropic = 2,
    Google = 3,
    Azure = 4,
    Custom = 99
}