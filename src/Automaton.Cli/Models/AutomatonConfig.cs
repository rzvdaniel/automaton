namespace Automaton.Cli.Models;

/// <summary>
/// Contents of automaton.json in a project root.
/// </summary>
public class AutomatonConfig
{
    public string AppId { get; set; } = string.Empty;
    public string Name  { get; set; } = string.Empty;

    /// <summary>
    /// Optional Automaton server URL for this project.
    /// </summary>
    public string? Server { get; set; }

    /// <summary>
    /// Optional build command to run before deploying (e.g. "npm run build").
    /// </summary>
    public string? Build { get; set; }

    /// <summary>
    /// Directory to upload as the frontend (default: tries dist/, then frontend/, then root).
    /// </summary>
    public string? DeployDir { get; set; }
}
