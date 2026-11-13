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
