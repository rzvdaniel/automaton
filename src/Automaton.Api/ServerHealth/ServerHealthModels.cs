using Automaton.Core.Models;

namespace Automaton.Api.ServerHealth;

public sealed record ServerVersionInfo(string? Version, string? LatestVersion, bool? UpdateAvailable);

public sealed record ServerRunnerInfo(
    string Name, string Status, string? Version, DateTime? LastHeartbeatAtUtc, bool UpgradeAvailable);

public sealed record CertificateInfo(string Host, DateTime? NotAfterUtc, int? DaysRemaining, string? Error);

public sealed record ServerBackupInfo(bool Available, DateTimeOffset? LastAttemptAt, DateTimeOffset? LastSuccessAt,
    double? LastSuccessAgeHours, string? LastError);

/// <summary>Read-only server health served to apps with an approved <c>server:read</c> permission.</summary>
/// <param name="Host">The latest figures reported by the Machine Management Runner; null until it has reported.</param>
/// <param name="HostAgeSeconds">How old <paramref name="Host"/> is.</param>
public sealed record ServerHealthResponse(
    DateTime GeneratedAtUtc,
    ServerVersionInfo Server,
    MachineHostHealth? Host,
    int? HostAgeSeconds,
    IReadOnlyList<ServerRunnerInfo> Runners,
    IReadOnlyList<CertificateInfo> Certificates,
    ServerBackupInfo Backup);
