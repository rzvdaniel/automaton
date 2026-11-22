using System.Text.Json;
using Automaton.Api.Data;

namespace Automaton.Api.Services;

public enum ServerOsUpdateResult { Accepted, InProgress, Busy, InvalidScope, TriggerMissing, TriggerBlocked }

/// <param name="State">idle, requested (waiting for the host), running, succeeded, failed, refused or unsupported.</param>
public sealed record ServerOsUpdateStatus(
    bool TriggerAvailable,
    string State,
    string? Scope,
    DateTimeOffset? RequestedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int? UpgradedCount,
    bool? RestartRequired,
    string? RequestedBy,
    string? Message,
    string? LogTail,
    int? TotalCount = null,
    int? ProgressCount = null,
    string? CurrentPackage = null,
    string? ErrorLine = null,
    IReadOnlyList<string>? HeldBack = null);

/// <summary>
/// Installs the host's OS updates. Like backups, server updates and restarts, the API never runs apt: it drops a request
/// file that a root systemd path unit (automaton-os-update.path, installed by the installer and updater) turns into a
/// non-interactive apt upgrade that keeps existing configuration files. The host checks for a running backup or
/// server update again before it starts.
/// </summary>
public static class ServerOsUpdate
{
    public const string DefaultRequestsDirectory = "/app/os-update-requests";
    public const string DefaultStatusDirectory = "/app/os-update-status";
    public static readonly string[] Scopes = ["security", "all"];
    // A run that has not finished after this long is treated as stuck, not as a reason to keep refusing.
    private static readonly TimeSpan StuckAfter = TimeSpan.FromHours(3);

    public static async Task<ServerOsUpdateStatus> ReadAsync(IConfiguration configuration, DateTimeOffset now, CancellationToken token)
    {
        string state = "idle";
        string? scope = null, requestedBy = null, message = null, logTail = null;
        DateTimeOffset? requestedAt = null, finishedAt = null;
        int? upgraded = null, total = null, progress = null;
        string? current = null, errorLine = null;
        IReadOnlyList<string>? heldBack = null;
        bool? restartRequired = null;
        var requests = RequestsDirectory(configuration);
        try
        {
            var statusPath = Path.Combine(configuration["Installation:OsUpdateStatusDirectory"] ?? DefaultStatusDirectory, "status.json");
            if (File.Exists(statusPath))
            {
                await using var stream = File.OpenRead(statusPath);
                var file = await JsonSerializer.DeserializeAsync<StatusFile>(stream, JsonOptions, token);
                if (file is not null)
                {
                    state = file.State is "running" or "succeeded" or "failed" or "refused" or "unsupported" ? file.State : "idle";
                    // A run that never finished (host crash or reboot) must not look active forever.
                    if (state == "running" && file.StartedAtUtc is { } started && now - started > StuckAfter) state = "failed";
                    scope = file.Scope; requestedBy = file.RequestedBy; message = file.Message; logTail = file.LogTail;
                    requestedAt = file.RequestedAtUtc; finishedAt = file.FinishedAtUtc;
                    upgraded = file.UpgradedCount; restartRequired = file.RestartRequired;
                    total = file.TotalCount; progress = file.ProgressCount; current = file.CurrentPackage; errorLine = file.ErrorLine;
                    heldBack = file.HeldBack?.Where(name => !string.IsNullOrWhiteSpace(name)).Take(50).ToArray();
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            message = "The update status could not be read.";
        }
        var target = Path.Combine(requests, "request.json");
        if (File.Exists(target) || Directory.Exists(target)) state = "requested";
        return new(Directory.Exists(requests), state, scope, requestedAt, finishedAt, upgraded, restartRequired, requestedBy, message, logTail,
            total, progress, current, errorLine, heldBack);
    }

    /// <summary>True while an OS update is waiting for the host or running, so a restart or a second update must wait.</summary>
    public static async Task<bool> IsActiveAsync(IConfiguration configuration, DateTimeOffset now, CancellationToken token)
        => (await ReadAsync(configuration, now, token)).State is "requested" or "running";

    public static async Task<(ServerOsUpdateResult Result, string? Reason)> RequestAsync(
        ApplicationDbContext db, IConfiguration configuration, string requestedBy, string? scope, DateTimeOffset now, CancellationToken token)
    {
        scope ??= "security";
        if (!Scopes.Contains(scope, StringComparer.Ordinal)) return (ServerOsUpdateResult.InvalidScope, null);
        var requests = RequestsDirectory(configuration);
        if (!Directory.Exists(requests)) return (ServerOsUpdateResult.TriggerMissing, null);
        var target = Path.Combine(requests, "request.json");
        if (Directory.Exists(target)) return (ServerOsUpdateResult.TriggerBlocked, null);
        if (File.Exists(target) || await IsActiveAsync(configuration, now, token)) return (ServerOsUpdateResult.InProgress, null);
        if (await ServerRestart.BusyReasonAsync(db, configuration, now, token) is { } reason) return (ServerOsUpdateResult.Busy, reason);
        if ((await ServerRestart.ReadAsync(configuration, now, token)).State is "requested" or "scheduled")
            return (ServerOsUpdateResult.Busy, "A server restart is waiting. Try again when it has finished.");

        var temporary = Path.Combine(requests, $".request-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
            {
                requestedBy = InstallationUpdate.SafeLabel(requestedBy),
                requestedAtUtc = now,
                scope
            }), token);
            File.Move(temporary, target);
            return (ServerOsUpdateResult.Accepted, null);
        }
        catch (IOException) when (File.Exists(target)) { return (ServerOsUpdateResult.InProgress, null); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (ServerOsUpdateResult.TriggerBlocked, null);
        }
        finally
        {
            try { File.Delete(temporary); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string RequestsDirectory(IConfiguration configuration)
        => configuration["Installation:OsUpdateRequestsDirectory"] ?? DefaultRequestsDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class StatusFile
    {
        public string? State { get; init; }
        public string? Scope { get; init; }
        public string? RequestedBy { get; init; }
        public DateTimeOffset? RequestedAtUtc { get; init; }
        public DateTimeOffset? StartedAtUtc { get; init; }
        public DateTimeOffset? FinishedAtUtc { get; init; }
        public int? UpgradedCount { get; init; }
        public bool? RestartRequired { get; init; }
        public string? Message { get; init; }
        public string? LogTail { get; init; }
        public int? TotalCount { get; init; }
        public int? ProgressCount { get; init; }
        public string? CurrentPackage { get; init; }
        public string? ErrorLine { get; init; }
        public string[]? HeldBack { get; init; }
    }
}
