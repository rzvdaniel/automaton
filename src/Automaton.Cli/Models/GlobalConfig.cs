namespace Automaton.Cli.Models;

/// <summary>
/// CLI configuration stored either in ./.automaton/config.json for a project
/// or in ~/.automaton/config.json for shared global use.
/// All values can also be supplied via environment variables as fallback values.
/// </summary>
public class GlobalConfig
{
    /// <summary>Falls back to AUTOMATON_SERVER when stored config is missing.</summary>
    public string? Server { get; set; }

    /// <summary>Falls back to AUTOMATON_API_KEY when stored config is missing.</summary>
    public string? ApiKey { get; set; }
}
