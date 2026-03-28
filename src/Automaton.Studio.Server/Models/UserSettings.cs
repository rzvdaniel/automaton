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

    /// <summary>
    /// User's preferred default AI model ID (e.g. "gpt-4o").
    /// Falls back to the admin-configured global default when null.
    /// </summary>
    public string? DefaultAiModel { get; set; }

    /// <summary>
    /// Provider name that owns DefaultAiModel (e.g. "OpenAI").
    /// </summary>
    public string? DefaultAiProvider { get; set; }

    /// <summary>
    /// User's preferred planning AI model ID.
    /// Falls back to the admin-configured global planning model when null.
    /// </summary>
    public string? DefaultPlanningModel { get; set; }

    /// <summary>
    /// Provider name that owns DefaultPlanningModel.
    /// </summary>
    public string? DefaultPlanningProvider { get; set; }
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

    private string? _defaultAiModel;
    private string? _defaultAiProvider;
    private string? _defaultPlanningModel;
    private string? _defaultPlanningProvider;

    /// <summary>
    /// Whether DefaultAiModel was explicitly included in the request payload.
    /// </summary>
    public bool HasDefaultAiModel { get; private set; }

    /// <summary>
    /// Whether DefaultAiProvider was explicitly included in the request payload.
    /// </summary>
    public bool HasDefaultAiProvider { get; private set; }

    /// <summary>
    /// Whether DefaultPlanningModel was explicitly included in the request payload.
    /// </summary>
    public bool HasDefaultPlanningModel { get; private set; }

    /// <summary>
    /// Whether DefaultPlanningProvider was explicitly included in the request payload.
    /// </summary>
    public bool HasDefaultPlanningProvider { get; private set; }

    public string? DefaultAiModel
    {
        get => _defaultAiModel;
        set
        {
            HasDefaultAiModel = true;
            _defaultAiModel = value;
        }
    }

    public string? DefaultAiProvider
    {
        get => _defaultAiProvider;
        set
        {
            HasDefaultAiProvider = true;
            _defaultAiProvider = value;
        }
    }

    public string? DefaultPlanningModel
    {
        get => _defaultPlanningModel;
        set
        {
            HasDefaultPlanningModel = true;
            _defaultPlanningModel = value;
        }
    }

    public string? DefaultPlanningProvider
    {
        get => _defaultPlanningProvider;
        set
        {
            HasDefaultPlanningProvider = true;
            _defaultPlanningProvider = value;
        }
    }
}
