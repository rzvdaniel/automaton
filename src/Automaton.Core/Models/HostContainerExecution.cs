namespace Automaton.Core.Models;

public static class HostContainerOperationKinds
{
    public const string ResolveImage = "resolve-image";
    public const string RunBuild = "run-build";
    public const string CreateApplicationContainer = "create-application-container";
    public const string StartContainer = "start-container";
    public const string InspectContainer = "inspect-container";
    public const string GetContainerLogs = "get-container-logs";
    public const string RemoveContainer = "remove-container";
}

public sealed record HostContainerScope(
    Guid HostedRunnerId,
    Guid RunnerHostId,
    Guid AppId,
    Guid DeploymentTargetId);

public sealed record HostedRunnerWorkspaceRef(string Id, string RelativePath);

public sealed record HostContainerResourceRef(
    string WorkspaceId,
    string ArtifactId,
    string DataId);

public sealed record HostContainerExecutionRequest
{
    public HostContainerScope Scope { get; init; } = new(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
    public string Operation { get; init; } = string.Empty;
    public string? RequestedImage { get; init; }
    public string? ContainerId { get; init; }
    public ApplicationCommandSpec? Command { get; init; }
    public int ContainerPort { get; init; }
    public int HostPort { get; init; }
    public int TimeoutSeconds { get; init; } = 900;
    public HostContainerResourceRef? Resources { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}

public sealed record HostContainerExecutionResponse
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public string RequestedImage { get; init; } = string.Empty;
    public string ResolvedImage { get; init; } = string.Empty;
    public string ContainerId { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public bool Running { get; init; }
    public string Logs { get; init; } = string.Empty;
}
