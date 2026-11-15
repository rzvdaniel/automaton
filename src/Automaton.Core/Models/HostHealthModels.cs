namespace Automaton.Core.Models;

/// <summary>
/// Host figures the Machine Management Runner measures every few minutes. Values it cannot read are null.
/// </summary>
public sealed record MachineHostHealth(
    DateTime MeasuredAtUtc,
    string? OsName,
    string? OsVersion,
    long? UptimeSeconds,
    int? CpuCount,
    double? CpuPercent,
    double? Load1,
    double? Load5,
    double? Load15,
    long? MemoryTotalBytes,
    long? MemoryAvailableBytes,
    long? DiskTotalBytes,
    long? DiskAvailableBytes,
    long? DockerImagesBytes,
    long? DockerContainersBytes,
    long? DockerVolumesBytes,
    long? DockerBuildCacheBytes,
    int? UpdatesPending,
    int? SecurityUpdates,
    bool? RebootRequired,
    DateTime? RebootRequiredSinceUtc,
    string? RunnerVersion,
    long? DockerUnusedImagesBytes = null,
    IReadOnlyList<AppDiskUsage>? AppDisk = null);

/// <summary>
/// Disk one app uses on a Runner's machine. Values the Runner cannot read are null.
/// <c>ImageBytes</c> is the size of the Docker images its containers use (an image shared with other apps counts in full for each).
/// </summary>
public sealed record AppDiskUsage(
    Guid AppId,
    long? FilesBytes,
    long? DataBytes,
    long? LogsBytes,
    long? ContainerBytes,
    long? ImageBytes);
