using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Api.Services;

public sealed record InstallationUpdateRun(
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

public sealed record InstallationUpdateStatus(
    string? CurrentVersion, bool TriggerAvailable, InstallationUpdateRun? Run, string? Log);

public enum InstallationUpdateRequestResult { Accepted, InProgress, TriggerMissing }

/// <summary>
/// The API never updates the server. It drops a request file into a directory that a root systemd path unit on the
/// host watches (docker/install/update-from-request.sh), and reads the status and log files that script writes into
/// a read-only mount. Both directories are mounted by the installer's Compose file.
/// </summary>
public static partial class InstallationUpdate
{
    public const string DefaultRequestsDirectory = "/app/update-requests";
    public const string DefaultStatusDirectory = "/app/update-status";
    public const string Latest = "latest";
    // A host that rebooted during an update leaves the status at "running"; do not block updates forever.
    public static readonly TimeSpan StaleRunningAfter = TimeSpan.FromHours(3);

    public static bool IsValidVersion(string? version)
        => version == Latest || (version is not null && VersionPattern().IsMatch(version));

    public static async Task<InstallationUpdateStatus> ReadAsync(IConfiguration configuration, CancellationToken token)
    {
        var statusDirectory = StatusDirectory(configuration);
        InstallationUpdateRun? run = null;
        string? log = null;
        try
        {
            var statusPath = Path.Combine(statusDirectory, "status.json");
            if (File.Exists(statusPath))
            {
                await using var stream = File.OpenRead(statusPath);
                run = await JsonSerializer.DeserializeAsync<InstallationUpdateRun>(stream, JsonOptions, token);
            }
            var logPath = Path.Combine(statusDirectory, "update.log");
            if (File.Exists(logPath)) log = TailOf(await File.ReadAllTextAsync(logPath, token));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            run = new("unknown", null, null, null, null, null, null, null, null, "The update status could not be read.");
        }
        return new(configuration["Installation:Version"], Directory.Exists(RequestsDirectory(configuration)), run, log);
    }

    public static async Task<InstallationUpdateRequestResult> RequestAsync(
        IConfiguration configuration, string version, string requestedBy, DateTimeOffset now, CancellationToken token)
    {
        var requests = RequestsDirectory(configuration);
        if (!Directory.Exists(requests)) return InstallationUpdateRequestResult.TriggerMissing;
        var current = await ReadAsync(configuration, token);
        if (current.Run is { State: "running" } running && now - (running.StartedAtUtc ?? now) < StaleRunningAfter)
            return InstallationUpdateRequestResult.InProgress;

        var target = Path.Combine(requests, "request.json");
        if (File.Exists(target)) return InstallationUpdateRequestResult.InProgress;
        var temporary = Path.Combine(requests, $".request-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary,
                JsonSerializer.Serialize(new { version, requestedBy = SafeLabel(requestedBy), requestedAtUtc = now }), token);
            File.Move(temporary, target);
        }
        catch (IOException) when (File.Exists(target))
        {
            return InstallationUpdateRequestResult.InProgress;
        }
        finally
        {
            File.Delete(temporary);
        }
        return InstallationUpdateRequestResult.Accepted;
    }

    // The host script accepts only letters, digits and a few separators in this label.
    internal static string SafeLabel(string value)
        => UnsafeCharacters().Replace(value, "_") is { Length: > 0 } label ? label[..Math.Min(label.Length, 200)] : "unknown";

    private static string RequestsDirectory(IConfiguration configuration)
        => configuration["Installation:UpdateRequestsDirectory"] ?? DefaultRequestsDirectory;

    private static string StatusDirectory(IConfiguration configuration)
        => configuration["Installation:UpdateStatusDirectory"] ?? DefaultStatusDirectory;

    private static string TailOf(string log)
    {
        const int maxCharacters = 64 * 1024;
        return log.Length <= maxCharacters ? log : log[^maxCharacters..];
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex(@"^server-v[0-9]+\.[0-9]+\.[0-9]+$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"[^A-Za-z0-9@._+ -]")]
    private static partial Regex UnsafeCharacters();
}
