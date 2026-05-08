namespace Automaton.Cli.Models;

public sealed class SetupResult
{
    public bool Success { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<CommandDiagnosticCheck> Checks { get; init; } = [];

    public IReadOnlyList<string> NextSteps { get; init; } = [];

    public string? Server { get; init; }

    public string Storage { get; init; } = "local";

    public string? UserId { get; init; }

    public string? UserName { get; init; }

    public string? UserEmail { get; init; }
}