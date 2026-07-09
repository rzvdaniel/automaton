namespace Automaton.Cli.Models;

/// <summary>
/// CLI configuration stored either in ./.automaton/config.json for a project
/// or in ~/.automaton/config.json for shared global use.
/// Environment variables override stored values during command execution.
/// </summary>
public class GlobalConfig
{
    /// <summary>Overridden by AUTOMATON_SERVER when present.</summary>
    public string? Server { get; set; }

    /// <summary>Overridden by AUTOMATON_API_KEY when present.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Optional environment-specific linked app ID.</summary>
    public string? AppId { get; set; }
}
