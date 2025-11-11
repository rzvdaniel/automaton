namespace Automaton.Studio.Server.Models;

/// <summary>
/// Configuration for app card appearance and styling on the Apps page.
/// Stored as JSON to allow flexible addition of future appearance properties
/// without requiring database migrations.
/// </summary>
public class AppAppearance
{
    /// <summary>
    /// Background color of the app card (hex color code).
    /// </summary>
    public string BackgroundColor { get; set; } = "#ffffff";

    /// <summary>
    /// URL of the background image displayed on the app card.
    /// </summary>
    public string? BackgroundImageUrl { get; set; }

    /// <summary>
    /// Opacity level of the background image (0.0 to 1.0).
    /// </summary>
    public double BackgroundImageOpacity { get; set; } = 1.0;

    /// <summary>
    /// Font color of the app title (hex color code).
    /// </summary>
    public string TitleFontColor { get; set; } = "#ffffff";
}
