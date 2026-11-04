namespace Automaton.Cli.Models;

/// <summary>Versioned shared CLI credentials. Each profile keeps its server and key together.</summary>
public sealed class CliProfilesConfig
{
    public int Version { get; set; } = 2;
    public string? Current { get; set; }
    public Dictionary<string, GlobalConfig> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
