namespace Automaton.Cli.Models;

public static class AiGuidanceModes
{
    public const string App = "app";
    public const string Workspace = "workspace";
    public const string Project = "project";
}

public sealed class AiGuidanceSyncResult
{
    public string Mode { get; init; } = AiGuidanceModes.Project;

    public IReadOnlyList<string> GeneratedFiles { get; init; } = [];
}
