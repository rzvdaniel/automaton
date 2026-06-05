namespace Automaton.Cli.Models;

public sealed class PublishResult
{
    public bool Success { get; init; }

    public bool Cancelled { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string? AppId { get; init; }

    public string? AppName { get; init; }

    public string? Slug { get; init; }

    public string? Url { get; init; }

    public string? StudioAppUrl { get; init; }

    public bool RequiresAuthentication { get; init; }

    public bool IsForkable { get; init; }

    public IReadOnlyList<CommandDiagnosticCheck> Checks { get; init; } = [];

    public IReadOnlyList<string> NextSteps { get; init; } = [];
}
