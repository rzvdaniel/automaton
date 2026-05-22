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

    public FrontendConfig? Frontend { get; set; }

    public BackendConfig? Backend { get; set; }

    public DeployScopeConfig? Source { get; set; }
}

public sealed class FrontendConfig
{
    /// <summary>
    /// Optional build command to run before deploying the frontend runtime.
    /// Example: "cd frontend && npm install && npm run build".
    /// </summary>
    public string? Build { get; set; }

    /// <summary>
    /// Required frontend deploy directory relative to the project root.
    /// Example: "frontend/dist" or "." for explicit no-build static apps.
    /// </summary>
    public string? DeployDir { get; set; }

    public List<string>? Include { get; set; }

    public List<string>? Exclude { get; set; }
}

public sealed class BackendConfig
{
    /// <summary>
    /// Backend deploy directory relative to the project root.
    /// Example: "backend".
    /// </summary>
    public string? DeployDir { get; set; }

    public List<string>? Include { get; set; }

    public List<string>? Exclude { get; set; }
}

public sealed class DeployScopeConfig
{
    public List<string>? Include { get; set; }

    public List<string>? Exclude { get; set; }
}
