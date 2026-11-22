namespace Automaton.Cli.Models;

/// <summary>The last server update run, as reported by the host (<c>GET /api/install/update</c>).</summary>
internal sealed record ServerUpdateRun(
    string State,
    string? RequestedVersion,
    string? FromVersion,
    string? ToVersion,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int? ExitCode,
    string? RecoveryBackup,
    string? RequestedBy,
    string? Message);

internal sealed record ServerUpdateStatus(
    string? CurrentVersion, bool TriggerAvailable, ServerUpdateRun? Run, string? Log,
    bool Stale = false, bool RequestPending = false, int? RequestAgeSeconds = null, string? Hint = null);

/// <summary>Backup state reported by the host (<c>GET /api/install/backups</c>).</summary>
internal sealed record ServerBackupStatus(
    bool Available, DateTimeOffset? LastAttemptAt, DateTimeOffset? LastSuccessAt, string? LastError, int Keep,
    DateTimeOffset? NextRunAt = null, bool TriggerAvailable = false, bool RequestPending = false);
