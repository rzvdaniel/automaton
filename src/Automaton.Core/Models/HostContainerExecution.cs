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

public static class HostContainerResourceKinds
{
    public const string Source = "source";
    public const string Artifact = "artifact";
    public const string ApplicationData = "data";
}

public sealed record HostContainerScope(
    Guid HostedRunnerId,
    Guid RunnerHostId,
    Guid AppId,
    Guid DeploymentTargetId,
    Guid DeploymentAttemptId = default);

public sealed record HostContainerResourceRef(
    string SourceKind,
    string ArtifactKind,
    string DataKind);

public sealed record HostContainerExecutionRequest
{
    public HostContainerScope Scope { get; init; } = new(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
    public string Operation { get; init; } = string.Empty;
    public string? RequestedImage { get; init; }
    public string? ContainerId { get; init; }
    public Guid CandidateId { get; init; }
    public ApplicationStorageSpec? Storage { get; init; }
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
    public bool Exists { get; init; }
    public string? HostAddress { get; init; }
    public int? HostPort { get; init; }
    public int? ContainerPort { get; init; }
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    public string Logs { get; init; } = string.Empty;
}
