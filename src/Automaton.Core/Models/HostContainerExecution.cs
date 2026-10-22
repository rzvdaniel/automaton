using System.Text.Json.Serialization;

namespace Automaton.Core.Models;

public static class HostContainerOperationKinds
{
    public const string ResolveImage = "resolve-image";
    public const string RunBuild = "run-build";
    public const string CreateApplicationContainer = "create-application-container";
    public const string ApplyComposeProject = "apply-compose-project";
    public const string InspectComposeProject = "inspect-compose-project";
    public const string RemoveComposeProject = "remove-compose-project";
    public const string StartContainer = "start-container";
    public const string InspectContainer = "inspect-container";
    public const string GetContainerLogs = "get-container-logs";
    public const string RemoveContainer = "remove-container";
    public const string RemoveTargetContainers = "remove-target-containers";
    public const string PrepareActionDependencies = "prepare-action-dependencies";
    public const string ExecuteAction = "execute-action";
    public const string RemoveActionContainer = "remove-action-container";
}

public static class HostContainerExecutionCommandKinds
{
    public const string InfrastructureActionKey = "host-container-execution";
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
    Guid DeploymentAttemptId = default,
    Guid MachineRunnerId = default);

public sealed record HostContainerResourceRef(
    string SourceKind,
    string ArtifactKind,
    string DataKind);

public sealed record HostContainerResourceLimits(decimal CpuLimitCores, int MemoryLimitMb, int PidsLimit)
{
    public bool IsValid() => CpuLimitCores is >= 0.10m and <= 8.0m
        && MemoryLimitMb is >= 256 and <= 16_384
        && PidsLimit is >= 32 and <= 4096;
}

public sealed record ActionContainerPolicy(
    HostContainerResourceLimits Limits,
    int SharedMemoryMb,
    int DockerLogMaxSizeMb,
    int DockerLogMaxFiles,
    string RuntimeImage)
{
    public int MinimumFreeDiskMb { get; init; } = 1024;

    public bool IsValid() => Limits.IsValid()
        && SharedMemoryMb is >= 16 and <= 1024
        && DockerLogMaxSizeMb is >= 1 and <= 1024
        && DockerLogMaxFiles is >= 1 and <= 20
        && MinimumFreeDiskMb is >= 256 and <= 1_048_576
        && !string.IsNullOrWhiteSpace(RuntimeImage)
        && !RuntimeImage.Any(char.IsWhiteSpace)
        && !RuntimeImage.Contains('\0');
}

public sealed record HostContainerExecutionRequest
{
    public Guid OperationId { get; init; }
    public HostContainerScope Scope { get; init; } = new(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
    public string Operation { get; init; } = string.Empty;
    public string? RequestedImage { get; init; }
    public string? ContainerId { get; init; }
    public string? ActionKey { get; init; }
    public Guid ActionExecutionId { get; init; }
    public Guid ActionStagingId { get; init; }
    public string? ActionInputJson { get; init; }
    public Guid CandidateId { get; init; }
    public ApplicationStorageSpec? Storage { get; init; }
    public ApplicationComposeSpec? Compose { get; init; }
    public ApplicationCommandSpec? Command { get; init; }
    public string? User { get; init; }
    public int ContainerPort { get; init; }
    public int HostPort { get; init; }
    public int TimeoutSeconds { get; init; } = 900;
    public HostContainerResourceRef? Resources { get; init; }
    public HostContainerResourceLimits? ContainerLimits { get; init; }
    public int? SharedMemoryMb { get; init; }
    public int? DockerLogMaxSizeMb { get; init; }
    public int? DockerLogMaxFiles { get; init; }
    public int? MinimumFreeDiskMb { get; init; }
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
    public string Output { get; init; } = string.Empty;
    public int? ExitCode { get; init; }
    public bool OutputExceededLimit { get; init; }
    public bool TimedOut { get; init; }
}

public sealed record HostContainerExecutionOperationResponse(
    Guid OperationId,
    bool Completed,
    HostContainerExecutionResponse? Result);
