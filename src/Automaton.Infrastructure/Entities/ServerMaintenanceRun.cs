namespace Automaton.Api.Entities;

public static class ServerMaintenanceKinds
{
    public const string Backup = "backup";
    public const string OsUpdate = "os-update";
    public const string Restart = "restart";
    public const string RunnerUpgrade = "runner-upgrade";
}

public static class ServerMaintenanceStates
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Refused = "refused";
}

/// <summary>One run of a maintenance task (backup, OS updates, server restart, Runner upgrade) with who started it and how it ended.</summary>
public sealed class ServerMaintenanceRun
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    /// <summary>The Runner an upgrade ran on; null for tasks of the server itself.</summary>
    public Guid? RunnerId { get; set; }
    public string? RequestedBy { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string State { get; set; } = ServerMaintenanceStates.Running;
    public string? Message { get; set; }
    /// <summary>Packages upgraded by an OS update run.</summary>
    public int? UpgradedCount { get; set; }
}
