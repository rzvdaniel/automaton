namespace Automaton.Core.Models;

public sealed record HostedRunnerStorageTelemetry(
    Guid HostedRunnerId,
    DateTime MeasuredAtUtc,
    long SystemBytes,
    long WorkspaceBytes,
    long LogsBytes,
    long HomeBytes,
    long UserStorageBytes,
    long? TotalBytes,
    long MeasurementDurationMs,
    bool Success,
    string? Error,
    string? RunnerVersion);

public sealed record RunnerHostStorageTelemetry(
    Guid RunnerHostId,
    DateTime MeasuredAtUtc,
    string FilesystemPath,
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes,
    string? DockerDataRoot,
    long? DockerDataRootTotalBytes,
    long? DockerDataRootAvailableBytes,
    bool Success,
    string? Error,
    string? InfrastructureRunnerVersion,
    bool ImageStoreFilesystemBounded = false);
