using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Models;

/// <summary>
/// Unified user settings model containing all user preferences
/// </summary>
public class UserSettingsDto
{
    /// <summary>
    /// Theme preference: light, dark, or system.
    /// Default: system
    /// </summary>
    [RegularExpression("^(light|dark|system)$", ErrorMessage = "Theme must be 'light', 'dark', or 'system'")]
    public string Theme { get; set; } = "system";

    /// <summary>
    /// Editor font size in pixels.
    /// Default: 14, Range: 10-24
    /// </summary>
    [Range(10, 24, ErrorMessage = "Font size must be between 10 and 24")]
    public int FontSize { get; set; } = 14;
}

/// <summary>
/// Request model for updating user settings (partial update supported)
/// Only provided fields will be updated
/// </summary>
public class UpdateUserSettingsRequest
{
    /// <summary>
    /// Theme preference: light, dark, or system (optional)
    /// </summary>
    [RegularExpression("^(light|dark|system)$", ErrorMessage = "Theme must be 'light', 'dark', or 'system'")]
    public string? Theme { get; set; }

    /// <summary>
    /// Editor font size in pixels (optional)
    /// </summary>
    [Range(10, 24, ErrorMessage = "Font size must be between 10 and 24")]
    public int? FontSize { get; set; }
}
