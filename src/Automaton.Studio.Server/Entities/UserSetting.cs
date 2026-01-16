#nullable disable

using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// User-scoped key-value settings storage for user preferences
/// Values are stored as JSON strings for flexibility
/// </summary>
public class UserSetting
{
    /// <summary>
    /// Unique identifier for the setting record
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// The user this setting belongs to
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// Key identifier for the setting
    /// Example: "ai.maxIterations", "appearance.theme", etc.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string SettingKey { get; set; }

    /// <summary>
    /// JSON-serialized value of the setting
    /// Can store simple values or complex objects
    /// </summary>
    [Required]
    public string SettingValue { get; set; }

    /// <summary>
    /// Timestamp when the setting was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of last update
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
