namespace Automaton.Api.Models;

using Automaton.Api.Entities;

public sealed class HostedRunnerResources
{
    public decimal CpuLimit { get; set; } = 0.50m;
    public int MemoryLimitMb { get; set; } = 384;
    public int MemoryReservationMb { get; set; } = 256;
    public int DiskLimitMb { get; set; } = 512;
    public int PidsLimit { get; set; } = 256;
    public int SharedMemoryMb { get; set; } = 64;
    public int DockerLogMaxSizeMb { get; set; } = 10;
    public int DockerLogMaxFiles { get; set; } = 3;
    public int MaximumApps { get; set; } = 3;
}

public sealed class HostedRunnerSpecificationSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public Guid SourceSpecificationId { get; set; }
    public string SourceSpecificationCode { get; set; } = string.Empty;
    public string SourceSpecificationName { get; set; } = string.Empty;
    public string? SourceSpecificationDescription { get; set; }
    public int SourceSpecificationRevision { get; set; }
    public HostedRunnerResources Resources { get; set; } = new();
    public DateTime CapturedAtUtc { get; set; }
}

public static class HostedRunnerSpecificationSnapshotFactory
{
    public static HostedRunnerSpecificationSnapshot Create(
        HostedRunnerSpecification specification,
        DateTime capturedAtUtc)
    {
        return new HostedRunnerSpecificationSnapshot
        {
            SchemaVersion = specification.SchemaVersion,
            SourceSpecificationId = specification.Id,
            SourceSpecificationCode = specification.Code,
            SourceSpecificationName = specification.Name,
            SourceSpecificationDescription = specification.Description,
            SourceSpecificationRevision = specification.Revision,
            CapturedAtUtc = capturedAtUtc,
            Resources = CloneResources(specification.Resources)
        };
    }

    public static HostedRunnerResources CloneResources(HostedRunnerResources resources)
        => new()
        {
            CpuLimit = resources.CpuLimit,
            MemoryLimitMb = resources.MemoryLimitMb,
            MemoryReservationMb = resources.MemoryReservationMb,
            DiskLimitMb = resources.DiskLimitMb,
            PidsLimit = resources.PidsLimit,
            SharedMemoryMb = resources.SharedMemoryMb,
            DockerLogMaxSizeMb = resources.DockerLogMaxSizeMb,
            DockerLogMaxFiles = resources.DockerLogMaxFiles,
            MaximumApps = resources.MaximumApps
        };
}

public static class HostedRunnerSpecificationIds
{
    public static readonly Guid Free = new("e08b3e55-4f76-42ca-a814-281137fe6600");
}

public static class HostedRunnerPlatformSettingsIds
{
    public static readonly Guid Default = new("f4f6a22d-9a37-4e66-92f4-9f2b4f9a1d3c");
}

public static class HostedRunnerSpecificationLimits
{
    public const decimal MinimumCpuLimit = 0.10m;
    public const decimal MaximumCpuLimit = 8.0m;
    public const int MinimumMemoryLimitMb = 256;
    public const int MaximumMemoryLimitMb = 16_384;
    public const int MinimumMemoryReservationMb = 64;
    public const int MinimumDiskLimitMb = 512;
    public const int MaximumDiskLimitMb = 102_400;
    public const int MinimumPidsLimit = 32;
    public const int MaximumPidsLimit = 4096;
    public const int MinimumSharedMemoryMb = 16;
    public const int MaximumSharedMemoryMb = 1024;
    public const int MinimumDockerLogMaxSizeMb = 1;
    public const int MaximumDockerLogMaxSizeMb = 1024;
    public const int MinimumDockerLogMaxFiles = 1;
    public const int MaximumDockerLogMaxFiles = 20;
    public const int MinimumApps = 1;
    public const int MaximumApps = 100;
}
