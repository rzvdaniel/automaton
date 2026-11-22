using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>The one row of nightly cleanup settings for this server.</summary>
public sealed class ServerCleanupSettings
{
    public const int SingletonId = 1;
    [Key]
    public int Id { get; set; } = SingletonId;
    /// <summary>Time of day (UTC) in minutes after midnight. The default 05:00 is after the 03:00 backup.</summary>
    public int MinutesAfterMidnightUtc { get; set; } = 300;
    public int KeepDays { get; set; } = 7;
    public int JournalLimitMb { get; set; } = 200;
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>One cleanup of one host, scheduled or started by an administrator.</summary>
public sealed class ServerCleanupRun
{
    [Key]
    public Guid Id { get; set; }
    public Guid RunnerId { get; set; }
    [MaxLength(200)]
    public string RunnerName { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    /// <summary>running, succeeded or failed.</summary>
    [MaxLength(20)]
    public string Result { get; set; } = ServerCleanupResults.Running;
    public long BytesFreed { get; set; }
    /// <summary>scheduled or manual.</summary>
    [MaxLength(20)]
    public string Trigger { get; set; } = ServerCleanupTriggers.Scheduled;
    [MaxLength(2000)]
    public string? Message { get; set; }
}

public static class ServerCleanupResults
{
    public const string Running = "running", Succeeded = "succeeded", Failed = "failed";
}

public static class ServerCleanupTriggers
{
    public const string Scheduled = "scheduled", Manual = "manual";
}
