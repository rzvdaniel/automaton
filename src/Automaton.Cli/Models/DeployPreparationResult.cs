namespace Automaton.Cli.Models;

public sealed record RunnerSelectionItem(string Id, string Name, bool Connected);

public sealed class DeployPreparationResult
{
    public bool Success { get; init; }

    public bool RequiresRunnerSelection { get; init; }

    public IReadOnlyList<DeployCheckResult> Checks { get; init; } = [];

    public IReadOnlyList<string> NextSteps { get; init; } = [];

    public IReadOnlyList<RunnerSelectionItem> AvailableRunners { get; init; } = [];

    public string? RunnerId { get; init; }

    public string? RunnerName { get; init; }
}
