namespace Automaton.Cli.Models;

public sealed class DeployPreparationResult
{
    public bool Success { get; init; }

    public IReadOnlyList<DeployCheckResult> Checks { get; init; } = [];

    public IReadOnlyList<string> NextSteps { get; init; } = [];

    public string? RunnerId { get; init; }

    public string? RunnerName { get; init; }
}
