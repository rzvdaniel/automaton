namespace Automaton.Cli.Models;

/// <summary>
/// Global CLI configuration stored in ~/.automaton/config.json.
/// All values can also be supplied via environment variables (preferred in CI).
/// </summary>
public class GlobalConfig
{
    /// <summary>AUTOMATON_SERVER env var overrides this.</summary>
    public string? Server { get; set; }

    /// <summary>AUTOMATON_API_KEY env var overrides this.</summary>
    public string? ApiKey { get; set; }
}
