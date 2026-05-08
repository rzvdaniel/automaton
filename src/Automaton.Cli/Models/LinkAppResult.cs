namespace Automaton.Cli.Models;

public sealed class LinkAppResult
{
    public bool Success { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<CommandDiagnosticCheck> Checks { get; init; } = [];

    public IReadOnlyList<string> NextSteps { get; init; } = [];

    public string? AppId { get; init; }

    public string? Name { get; init; }

    public string? Server { get; init; }

    public string? ConfigPath { get; init; }
}