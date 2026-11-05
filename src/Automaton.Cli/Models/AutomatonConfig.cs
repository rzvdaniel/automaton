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

    public string? Server { get; set; }

    public ApplicationContract Application { get; set; } = ApplicationContractDefaults.Create();

    public DeployScopeConfig? Source { get; set; }

    /// <summary>Fields this CLI does not know (written by a newer CLI or by hand); kept when the file is rewritten.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class DeployScopeConfig
{
    public List<string>? Include { get; set; }

    public List<string>? Exclude { get; set; }
}
