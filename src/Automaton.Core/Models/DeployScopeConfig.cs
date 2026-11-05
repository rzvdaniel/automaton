using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Core.Models;

/// <summary>Include/exclude patterns that scope which source files an app deploys.</summary>
public sealed class DeployScopeConfig
{
    public List<string>? Include { get; set; }

    public List<string>? Exclude { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
