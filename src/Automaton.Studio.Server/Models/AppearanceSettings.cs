using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Models;

/// <summary>
/// User appearance settings
/// </summary>
public class AppearanceSettings
{
    /// <summary>
    /// Theme preference: light, dark, or system.
    /// Default: system
    /// </summary>
    [RegularExpression("^(light|dark|system)$", ErrorMessage = "Theme must be 'light', 'dark', or 'system'")]
    public string Theme { get; set; } = "system";
}

/// <summary>
/// Request model for updating appearance settings
/// </summary>
public class UpdateAppearanceSettingsRequest
{
    /// <summary>
    /// Theme preference: light, dark, or system
    /// </summary>
    [Required]
    [RegularExpression("^(light|dark|system)$", ErrorMessage = "Theme must be 'light', 'dark', or 'system'")]
    public string Theme { get; set; } = "system";
}
