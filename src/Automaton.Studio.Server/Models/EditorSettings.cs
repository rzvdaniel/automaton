using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Models;

/// <summary>
/// User-configurable editor settings
/// </summary>
public class EditorSettings
{
    /// <summary>
    /// Font size for code editors (Monaco).
    /// Default: 14, Range: 10-24
    /// </summary>
    [Range(10, 24, ErrorMessage = "FontSize must be between 10 and 24")]
    public int FontSize { get; set; } = 14;
}

/// <summary>
/// Request model for updating editor settings
/// </summary>
public class UpdateEditorSettingsRequest
{
    /// <summary>
    /// Font size for code editors
    /// </summary>
    [Range(10, 24, ErrorMessage = "FontSize must be between 10 and 24")]
    public int FontSize { get; set; } = 14;
}
