namespace Automaton.Cli.Models;

/// <summary>
/// CLI configuration stored either in ./.automaton/config.json for a project
/// or in ~/.automaton/config.json for shared global use.
/// All values can also be supplied via environment variables (preferred in CI).
/// </summary>
public class GlobalConfig
{
    /// <summary>AUTOMATON_SERVER env var overrides this.</summary>
    public string? Server { get; set; }

    /// <summary>AUTOMATON_API_KEY env var overrides this.</summary>
    public string? ApiKey { get; set; }

    /// <summary>AUTOMATON_PYTHON env var overrides this.</summary>
    public string? PythonPath { get; set; }
}
