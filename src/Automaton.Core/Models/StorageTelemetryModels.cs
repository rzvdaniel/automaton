namespace Automaton.Core.Models;

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

public sealed record MachineRunnerStorageTelemetry(
    DateTime MeasuredAtUtc,
    long TotalBytes,
    long AvailableBytes,
    bool Success,
    string? Error,
    string? RunnerVersion);
