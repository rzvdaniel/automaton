namespace Automaton.Cli.Models;

public sealed class ResolvedCliContext
{
    public string? EnvironmentName { get; init; }

    public string? Server { get; init; }

    public string? ApiKey { get; init; }

    public string? AppId { get; init; }
}
