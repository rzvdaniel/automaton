using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Database entity representing an AI model (gpt-4, claude-3-5-sonnet, gemini-pro, etc.)
/// Allows dynamic model management without code changes
/// </summary>
public class AiModel
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid AiProviderId { get; set; }

    [Required]
    [MaxLength(200)]
    public string ModelId { get; set; } = string.Empty; // "gpt-4-turbo", "claude-3-5-sonnet-20241022"

    [Required]
    [MaxLength(300)]
    public string DisplayName { get; set; } = string.Empty; // "GPT-4 Turbo", "Claude 3.5 Sonnet"

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// When true, this model is preferred for the planning phase (plan mode).
    /// The system will use this model when generating implementation plans, even if the user
    /// has selected a different model for implementation.
    /// Only one model per provider should have this flag set.
    /// </summary>
    public bool IsPlanningModel { get; set; } = false;

    public int MaxTokens { get; set; } = 4096;

    public bool SupportsStreaming { get; set; } = true;

    public bool SupportsTools { get; set; } = false;

    public bool SupportsVision { get; set; } = false;

    [Column(TypeName = "decimal(10, 6)")]
    public decimal? CostPer1MTokensInput { get; set; }

    [Column(TypeName = "decimal(10, 6)")]
    public decimal? CostPer1MTokensOutput { get; set; }

    public int SortOrder { get; set; } = 0;

    [Required]
    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(AiProviderId))]
    public virtual AiProvider Provider { get; set; } = null!;
}
