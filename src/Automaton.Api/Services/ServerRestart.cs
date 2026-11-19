using System.Globalization;
using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public enum ServerRestartResult { Accepted, InProgress, Busy, InvalidTime, TriggerMissing, TriggerBlocked }

/// <param name="State">idle, requested (waiting for the host), scheduled, or refused (the host declined the last request).</param>
public sealed record ServerRestartStatus(
    bool TriggerAvailable,
    string State,
    DateTimeOffset? LastRestartedAtUtc,
    DateTimeOffset? RestartAtUtc,
    string? RequestedBy,
    string? Message);

/// <summary>
/// Restarts the host. Like backups and server updates, the API never reboots anything: it drops a request file that a
/// root systemd path unit (automaton-restart.path, installed by the installer and updater) turns into a reboot,
/// now or at the requested time. The host re-checks for a running backup or update before it reboots.
/// </summary>
public static class ServerRestart
{
    public const string DefaultRequestsDirectory = "/app/restart-requests";
    public const string DefaultStatusDirectory = "/app/restart-status";
    public const string DefaultBootTimeFile = "/proc/stat";
    public static readonly TimeSpan MaxLeadTime = TimeSpan.FromDays(7);
    // A restart this close counts as "now"; the host also treats it so.
    public static readonly TimeSpan NowWindow = TimeSpan.FromSeconds(30);
    // Operations older than this are treated as stuck, not as a reason to keep the server up.
    private static readonly TimeSpan StuckAfter = TimeSpan.FromHours(6);

    public static async Task<ServerRestartStatus> ReadAsync(IConfiguration configuration, DateTimeOffset now, CancellationToken token)
    {
        var bootedAt = ReadBootTime(configuration);
        string state = "idle";
        DateTimeOffset? restartAt = null;
        string? requestedBy = null, message = null;
        var requestPath = Path.Combine(RequestsDirectory(configuration), "request.json");
        try
        {
            var statusPath = Path.Combine(configuration["Installation:RestartStatusDirectory"] ?? DefaultStatusDirectory, "status.json");
            if (File.Exists(statusPath))
            {
                await using var stream = File.OpenRead(statusPath);
                var file = await JsonSerializer.DeserializeAsync<StatusFile>(stream, JsonOptions, token);
                if (file is not null)
                {
                    // A status written before the last boot describes a restart that has already happened.
                    var current = bootedAt is null || file.RequestedAtUtc is null || file.RequestedAtUtc > bootedAt;
                    if (current)
                    {
                        requestedBy = file.RequestedBy;
                        message = file.Message;
                        if (file.State == "scheduled" && file.RestartAtUtc is { } at && at > now) { state = "scheduled"; restartAt = at; }
                        else if (file.State == "refused") state = "refused";
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            message = "The restart status could not be read.";
        }
        if (File.Exists(requestPath) || Directory.Exists(requestPath)) state = "requested";
        return new(Directory.Exists(RequestsDirectory(configuration)), state, bootedAt, restartAt, requestedBy, message);
    }

    /// <summary>Why the server must not restart right now (a backup, deploy, server update or Runner upgrade is active), or null.</summary>
    public static async Task<string?> BusyReasonAsync(
        ApplicationDbContext db, IConfiguration configuration, DateTimeOffset now, CancellationToken token)
    {
        if ((await InstallationBackups.ReadAsync(configuration, token)).RequestPending || BackupIsRunning(configuration, now))
            return "A backup is running. Try again when it has finished.";
        var update = await InstallationUpdate.ReadAsync(configuration, now, token);
        if (update.RequestPending || (update.Run?.State is "starting" or "running" && !update.Stale))
            return "A server update is running. Try again when it has finished.";
        var since = now.UtcDateTime - StuckAfter;
        if (await db.DeploymentOperations.AsNoTracking().AnyAsync(operation =>
                (operation.Status == DeploymentOperationStatuses.Pending || operation.Status == DeploymentOperationStatuses.Running)
                && operation.CompletedAtUtc == null && operation.CreatedAtUtc > since, token))
            return "An app deploy is running. Try again when it has finished.";
        string[] finished =
        [
            InfrastructureRunnerUpdateOperationStatuses.Succeeded, InfrastructureRunnerUpdateOperationStatuses.RolledBack,
            InfrastructureRunnerUpdateOperationStatuses.Failed, InfrastructureRunnerUpdateOperationStatuses.TimedOut
        ];
        if (await db.InfrastructureRunnerUpdateOperations.AsNoTracking().AnyAsync(operation =>
                operation.FinishedAtUtc == null && !finished.Contains(operation.Status) && operation.RequestedAtUtc > since, token))
            return "A Runner upgrade is running. Try again when it has finished.";
        return null;
    }

    public static async Task<(ServerRestartResult Result, string? Reason)> RequestAsync(
        ApplicationDbContext db, IConfiguration configuration, string requestedBy, DateTimeOffset? restartAt,
        DateTimeOffset now, CancellationToken token)
    {
        var requests = RequestsDirectory(configuration);
        if (!Directory.Exists(requests)) return (ServerRestartResult.TriggerMissing, null);
        if (restartAt is { } at && (at - now > MaxLeadTime || now - at > TimeSpan.FromMinutes(5)))
            return (ServerRestartResult.InvalidTime, null);
        if (restartAt is { } soon && soon - now <= NowWindow) restartAt = null;
        var target = Path.Combine(requests, "request.json");
        if (Directory.Exists(target)) return (ServerRestartResult.TriggerBlocked, null);
        if (File.Exists(target)) return (ServerRestartResult.InProgress, null);
        var current = await ReadAsync(configuration, now, token);
        if (current.State == "scheduled") return (ServerRestartResult.InProgress, null);
        if (await BusyReasonAsync(db, configuration, now, token) is { } reason) return (ServerRestartResult.Busy, reason);

        var temporary = Path.Combine(requests, $".request-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
            {
                requestedBy = InstallationUpdate.SafeLabel(requestedBy),
                requestedAtUtc = now,
                restartAtUtc = restartAt?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            }), token);
            File.Move(temporary, target);
            return (ServerRestartResult.Accepted, null);
        }
        catch (IOException) when (File.Exists(target)) { return (ServerRestartResult.InProgress, null); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (ServerRestartResult.TriggerBlocked, null);
        }
        finally
        {
            try { File.Delete(temporary); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    // backup.sh keeps this marker while it runs; one older than a backup can last is a leftover of a crash or reboot.
    private static bool BackupIsRunning(IConfiguration configuration, DateTimeOffset now)
    {
        var marker = Path.Combine(configuration["Installation:StatusDirectory"] ?? InstallationBackups.DefaultStatusDirectory, "backup.running");
        return File.Exists(marker) && now - File.GetLastWriteTimeUtc(marker) < StuckAfter;
    }

    /// <summary>When the host last booted: the btime line of /proc/stat, which a container shares with its host.</summary>
    internal static DateTimeOffset? ReadBootTime(IConfiguration configuration)
    {
        try
        {
            foreach (var line in File.ReadLines(configuration["Installation:BootTimeFile"] ?? DefaultBootTimeFile))
            {
                if (!line.StartsWith("btime ", StringComparison.Ordinal)) continue;
                return long.TryParse(line.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private static string RequestsDirectory(IConfiguration configuration)
        => configuration["Installation:RestartRequestsDirectory"] ?? DefaultRequestsDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class StatusFile
    {
        public string? State { get; init; }
        public string? RequestedBy { get; init; }
        public DateTimeOffset? RequestedAtUtc { get; init; }
        public DateTimeOffset? RestartAtUtc { get; init; }
        public string? Message { get; init; }
    }
}
