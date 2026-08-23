namespace Automaton.Cli.Models;

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
}

public sealed class DeployScopeConfig
{
    public List<string>? Include { get; set; }

    public List<string>? Exclude { get; set; }
}
