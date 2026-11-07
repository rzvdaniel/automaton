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
    string? RunnerVersion);
