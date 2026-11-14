using System.Text.Json;

namespace Automaton.Core.Models;

/// <summary>App Action management payload fetched over authenticated HTTPS, never placed in RabbitMQ.</summary>
public sealed record AppActionCommand(string Kind, JsonElement Payload)
{
    public static AppActionCommand Create<T>(string kind, T payload)
        => new(kind, JsonSerializer.SerializeToElement(payload));
    public T Read<T>() => Payload.Deserialize<T>() ?? throw new InvalidDataException("App Action payload is required.");
}

/// <summary>Asks a Machine Management Runner to replace itself with the script the server prepared for it.</summary>
public sealed record UpgradeRunnerRequest(Guid RunnerId, string Script);

/// <summary>Whether the Runner's helper container has started an upgrade.</summary>
public sealed record UpgradeRunnerStarted(bool Started);

/// <summary>State of the helper container that upgrades a Runner: none, running, succeeded or failed.</summary>
public sealed record RunnerUpgradeHelperStatus(string State, string? Log);

/// <summary>Asks a Machine Management Runner to free host disk space: unused Docker images and build cache older than <paramref name="KeepDays"/> days, and archived journal files above <paramref name="JournalLimitMb"/> MB.</summary>
public sealed record HostCleanupRequest(int KeepDays, int JournalLimitMb);

/// <summary>What a host cleanup freed, in bytes, and a short plain-text summary of each step.</summary>
public sealed record HostCleanupResult(long BytesFreed, string Summary);

/// <summary>Asks a Machine Management Runner for the last <paramref name="Lines"/> lines of one app's container output.</summary>
public sealed record ReadAppLogsRequest(Guid AppId, Guid DeploymentTargetId, int Lines);

/// <summary>The recent output of one app's container; <paramref name="Running"/> is false when it has no container.</summary>
public sealed record ReadAppLogsResult(Guid AppId, Guid DeploymentTargetId, bool Running, string Text);

/// <summary>Docker's own disk figures on one machine: what images, containers, volumes and build cache use, in bytes.</summary>
public sealed record HostDiskReport(long ImagesBytes, long ContainersBytes, long VolumesBytes, long BuildCacheBytes, long ReclaimableBytes);

/// <summary>What installing the OS security updates on one machine did: whether it succeeded, a short tail of the installer's output and whether the machine now needs a restart.</summary>
public sealed record HostOsUpdateResult(bool Succeeded, string Summary, bool RestartRequired);

/// <summary>The Runner accepted a restart of the machine; it happens a minute later, so the answer can still be delivered.</summary>
public sealed record HostRebootResult(bool Scheduled);
