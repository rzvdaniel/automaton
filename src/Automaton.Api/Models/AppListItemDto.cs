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
    public bool CanLaunchPublicly { get; init; }
    public string? PublicUrl { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public bool CanDelete { get; init; }
    /// <summary>The caller may deploy, pause and resume the app.</summary>
    public bool CanUpdate { get; init; }
    /// <summary>The app is paused: stopped, schedules off, address answers "paused".</summary>
    public bool IsPaused { get; init; }
    public AppListRunnerDto? Runner { get; init; }
    public AppListEnvironmentDto? LiveEnvironment { get; init; }
    public AppListRuntimeDto Runtime { get; init; } = new();
    /// <summary>The app has no web runtime or static site, only Actions.</summary>
    public bool ActionsOnly { get; init; }
    public AppListActionRunDto? LastActionRun { get; init; }
    /// <summary>The soonest run of an enabled Action schedule, for Action-only apps.</summary>
    public DateTime? NextActionRunAtUtc { get; init; }

    public AppListItemDto WithActionRuns(AppListActionRunDto? run, DateTime? nextRunAtUtc)
        => new()
        {
            Id = Id, Name = Name, Summary = Summary, Description = Description, ThumbnailUrl = ThumbnailUrl, Tags = Tags,
            IsPublished = IsPublished, CanLaunchPublicly = CanLaunchPublicly, PublicUrl = PublicUrl, UpdatedAtUtc = UpdatedAtUtc,
            CanDelete = CanDelete, CanUpdate = CanUpdate, IsPaused = IsPaused, Runner = Runner, LiveEnvironment = LiveEnvironment, Runtime = Runtime,
            ActionsOnly = ActionsOnly, LastActionRun = run, NextActionRunAtUtc = nextRunAtUtc,
        };
}

public sealed class AppListActionRunDto
{
    public string ModuleKey { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class AppListEnvironmentDto
{
    public string Name { get; init; } = "Live";
    public string Status { get; init; } = "Unassigned";
    public bool IsHostedRunner { get; init; }
    public string? ApplicationProfile { get; init; }
    public bool RunnerOnline { get; init; }
    public bool GenericRuntimeDeployed { get; init; }
    public DateTime? LastSuccessfulDeploymentAtUtc { get; init; }
    public bool IsStaticHosting { get; init; }
    /// <summary>Path relative to the app-specific private host, not the shared apps domain.</summary>
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
    public bool GenericRuntimeDeployed { get; init; }
}
