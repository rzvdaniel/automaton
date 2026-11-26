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
    string? RunnerVersion,
    long? DockerUnusedImagesBytes = null,
    IReadOnlyList<AppDiskUsage>? AppDisk = null,
    Fail2BanStatus? Fail2Ban = null,
    IReadOnlyList<HostDisk>? Disks = null,
    HostSecurityStatus? HostSecurity = null);

/// <summary>
/// One disk of the host: the main disk (mounted at <c>/</c>) and, when Docker keeps its data on another disk, that disk
/// at Docker's data root. <c>DiskTotalBytes</c>/<c>DiskAvailableBytes</c> stay the disk the apps' storage is on (what an app's Disk card shows).
/// </summary>
public sealed record HostDisk(string MountPoint, long TotalBytes, long AvailableBytes);

/// <summary>
/// Disk one app uses on a Runner's machine. Values the Runner cannot read are null.
/// <c>CpuPercent</c> (of one core) and <c>MemoryBytes</c> are the live use of its running containers; null when it has none running.
/// <c>ImageBytes</c> is the size of the Docker images its containers use (an image shared with other apps counts in full for each).
/// </summary>
public sealed record AppDiskUsage(
    Guid AppId,
    long? FilesBytes,
    long? DataBytes,
    long? LogsBytes,
    long? ContainerBytes,
    long? ImageBytes,
    double? CpuPercent = null,
    long? MemoryBytes = null);

/// <summary>
/// What fail2ban on the host reports, read from its database (never its socket). <c>Running</c> is false when fail2ban is
/// not installed or not started. A ban counts as current until its recorded expiry; a manual unban shows only after the
/// ban would have expired.
/// </summary>
public sealed record Fail2BanStatus(bool Running, IReadOnlyList<Fail2BanJail> Jails);

public sealed record Fail2BanJail(
    string Name, bool Enabled, int BannedLastHour, int BannedLast24Hours, IReadOnlyList<Fail2BanBan> CurrentBans);

public sealed record Fail2BanBan(string Ip, DateTime BannedAtUtc, DateTime? ExpiresAtUtc);

/// <summary>
/// What the host security check found on the server: SSH password login, ports open to the internet beyond the ones Automaton
/// needs, no firewall. Null on the report when the Runner could not run the check (unknown, never "all good").
/// </summary>
public sealed record HostSecurityStatus(string? Firewall, IReadOnlyList<HostSecurityFinding> Findings);

/// <param name="Id">Stable key such as <c>ssh-password</c>, <c>ssh-root-password</c>, <c>no-firewall</c> or <c>port-tcp-5432</c>.</param>
public sealed record HostSecurityFinding(string Id, string Title, string Detail);
