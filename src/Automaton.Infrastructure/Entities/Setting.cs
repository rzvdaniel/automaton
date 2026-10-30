#nullable enable

using Automaton;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>
/// Generic key-value settings storage for application configuration
/// Values are stored as JSON strings for flexibility
/// </summary>
public class Setting
{
    /// <summary>
    /// Unique key identifier for the setting
    /// Example: "Apps.DeployRules", "EmailConfiguration", etc.
    /// </summary>
    [Key]
    [Required]
    [MaxLength(255)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized value of the setting
    /// Can store simple strings or complex objects
    /// </summary>
    [Required]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of last update for audit purposes
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional description of what this setting controls
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }
}
