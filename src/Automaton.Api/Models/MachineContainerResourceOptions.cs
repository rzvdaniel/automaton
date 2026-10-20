using Automaton.Core.Models;

namespace Automaton.Api.Models;

/// <summary>Server-owned per-container bounds for work assigned to a customer machine.</summary>
public sealed class MachineContainerResourceOptions
{
    public const string SectionName = "MachineContainerResources";

    public decimal CpuLimitCores { get; set; } = 1.0m;
    public int MemoryLimitMb { get; set; } = 1024;
    public int PidsLimit { get; set; } = 256;
    public int SharedMemoryMb { get; set; } = 64;
    public int DockerLogMaxSizeMb { get; set; } = 20;
    public int DockerLogMaxFiles { get; set; } = 5;
    public int MinimumFreeDiskMb { get; set; } = 1024;
    public string ActionRuntimeImage { get; set; } = "python:3.12-slim";

    public ActionContainerPolicy ToActionContainerPolicy()
        => new(new(CpuLimitCores, MemoryLimitMb, PidsLimit), SharedMemoryMb,
            DockerLogMaxSizeMb, DockerLogMaxFiles, ActionRuntimeImage)
        { MinimumFreeDiskMb = MinimumFreeDiskMb };

    public bool IsValid() => new HostContainerResourceLimits(CpuLimitCores, MemoryLimitMb, PidsLimit).IsValid()
        && SharedMemoryMb is >= 16 and <= 1024
        && DockerLogMaxSizeMb is >= 1 and <= 1024
        && MinimumFreeDiskMb is >= 256 and <= 1_048_576
        && ToActionContainerPolicy().IsValid();
}
