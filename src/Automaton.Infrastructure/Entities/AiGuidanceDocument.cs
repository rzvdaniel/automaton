using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>
/// Runtime-managed AI guidance document used by Studio and CLI prompt harnesses.
/// Embedded guidance remains the seed/fallback source; database rows are the mutable runtime source.
/// </summary>
public class AiGuidanceDocument
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(120)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(260)]
    public string RelativePath { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Surface { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(4000)]
    public string? TagsJson { get; set; }

    [Required]
    public string ContentMarkdown { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public int Version { get; set; } = 1;

    [Required]
    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
