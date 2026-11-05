namespace Automaton.Cli.Models;

using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.Core.Models;

/// <summary>
/// Contents of automaton.json in a project root.
/// </summary>
public class AutomatonConfig
{
    public string? AppId { get; set; }

    public string Name  { get; set; } = string.Empty;
    public string? Slug { get; set; }

    /// <summary>One line shown on the app's marketplace card; used only while the app has no summary of its own.</summary>
    public string? Summary { get; set; }

    /// <summary>Longer marketplace text; used only while the app has no description of its own.</summary>
    public string? Description { get; set; }

    public string? Server { get; set; }

    public ApplicationContract Application { get; set; } = ApplicationContractDefaults.Create();

    public DeployScopeConfig? Source { get; set; }

    /// <summary>Fields this CLI does not know (written by a newer CLI or by hand); kept when the file is rewritten.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
