using Automaton.Core.Enums;

namespace Automaton.Api.Models;

public sealed class AppListItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? ThumbnailUrl { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public bool IsPublished { get; init; }
    public string? PublicUrl { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public bool CanDelete { get; init; }
    public AppListRunnerDto? Runner { get; init; }
    public AppListEnvironmentDto? LiveEnvironment { get; init; }
    public AppListRuntimeDto Runtime { get; init; } = new();
}

public sealed class AppListEnvironmentDto
{
    public string Name { get; init; } = "Live";
    public string Status { get; init; } = "Unassigned";
    public bool IsHostedRunner { get; init; }
    public string? ApplicationProfile { get; init; }
    public bool RunnerOnline { get; init; }
    public bool FrontendDeployed { get; init; }
    public bool BackendDeployed { get; init; }
    public string RoutePath { get; init; } = string.Empty;
}

public sealed class AppListRunnerDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public RunnerStatus ConnectivityStatus { get; init; }
}

public sealed class AppListRuntimeDto
{
    public bool FrontendAvailable { get; init; }
    public bool BackendAvailable { get; init; }
    public DateTime? FrontendDeployedAtUtc { get; init; }
    public DateTime? BackendDeployedAtUtc { get; init; }
}
