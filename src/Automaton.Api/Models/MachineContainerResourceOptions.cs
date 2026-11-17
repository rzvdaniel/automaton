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

    // Tighter bounds for the platform's `home-card` runs, so a runaway card script cannot take much from the apps around it.
    public decimal HomeCardCpuLimitCores { get; set; } = 0.25m;
    public int HomeCardMemoryLimitMb { get; set; } = 128;
    public int HomeCardPidsLimit { get; set; } = 64;
    public int HomeCardTimeoutSeconds { get; set; } = 30;

    public ActionContainerPolicy ToActionContainerPolicy()
        => new(new(CpuLimitCores, MemoryLimitMb, PidsLimit), SharedMemoryMb,
            DockerLogMaxSizeMb, DockerLogMaxFiles, ActionRuntimeImage)
        { MinimumFreeDiskMb = MinimumFreeDiskMb };

    /// <summary>The policy for a <c>home-card</c> run: the same image and log settings, lighter limits.</summary>
    public ActionContainerPolicy ToHomeCardContainerPolicy()
        => ToActionContainerPolicy() with { Limits = new(HomeCardCpuLimitCores, HomeCardMemoryLimitMb, HomeCardPidsLimit) };

    public bool IsValid() => new HostContainerResourceLimits(CpuLimitCores, MemoryLimitMb, PidsLimit).IsValid()
        && SharedMemoryMb is >= 16 and <= 1024
        && DockerLogMaxSizeMb is >= 1 and <= 1024
        && MinimumFreeDiskMb is >= 256 and <= 1_048_576
        && ToHomeCardContainerPolicy().IsValid()
        && HomeCardTimeoutSeconds is >= 1 and <= 600
        && ToActionContainerPolicy().IsValid();
}
