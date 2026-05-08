namespace Automaton.Cli.Models;

public sealed class SyncAiResult
{
    public bool Success { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<CommandDiagnosticCheck> Checks { get; init; } = [];

    public IReadOnlyList<string> NextSteps { get; init; } = [];

    public string Mode { get; init; } = AiGuidanceModes.Project;

    public string? ManifestPath { get; init; }

    public IReadOnlyList<string> GeneratedFiles { get; init; } = [];
}