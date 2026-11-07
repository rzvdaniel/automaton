using System.Text;
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

/// <param name="Stale">The last run still says starting/running but began long ago (for example the host rebooted).</param>
/// <param name="RequestPending">A request file is waiting for the host to pick it up.</param>
/// <param name="Hint">Set when the host trigger looks stuck.</param>
public sealed record InstallationUpdateStatus(
    string? CurrentVersion,
    bool TriggerAvailable,
    InstallationUpdateRun? Run,
    string? Log,
    bool Stale,
    bool RequestPending,
    int? RequestAgeSeconds,
    string? Hint);

public enum InstallationUpdateRequestResult { Accepted, InProgress, TriggerMissing, TriggerBlocked, Downgrade }

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
    public static readonly TimeSpan PickupTimeout = TimeSpan.FromMinutes(2);
    private const int MaxLogBytes = 64 * 1024;

    public static bool IsValidVersion(string? version)
        => version == Latest || (version is not null && VersionPattern().IsMatch(version));

    /// <summary>True when the requested release is older than the installed one.</summary>
    public static bool IsDowngrade(string? installed, string requested)
        => requested != Latest && Parse(installed) is { } current && Parse(requested) is { } target && target < current;

    public static async Task<InstallationUpdateStatus> ReadAsync(
        IConfiguration configuration, DateTimeOffset now, CancellationToken token)
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
            if (File.Exists(logPath)) log = await ReadTailAsync(logPath, token);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            run = new("unknown", null, null, null, null, null, null, null, null, "The update status could not be read.");
        }

        var requestPath = Path.Combine(RequestsDirectory(configuration), "request.json");
        var pending = File.Exists(requestPath) || Directory.Exists(requestPath);
        int? age = pending ? (int)Math.Max(0, (now - Directory.GetLastWriteTimeUtc(requestPath)).TotalSeconds) : null;
        var stalled = age is { } seconds && seconds > PickupTimeout.TotalSeconds;
        var hint = stalled
            ? "The server has not picked up the update request. On the server, check: systemctl status automaton-update.path"
            : null;
        var stale = IsActive(run) && now - (run!.StartedAtUtc ?? now) >= StaleRunningAfter;
        return new(configuration["Installation:Version"], Directory.Exists(RequestsDirectory(configuration)),
            run, log, stale, pending, age, hint);
    }

    public static async Task<InstallationUpdateRequestResult> RequestAsync(
        IConfiguration configuration, string version, bool allowDowngrade, string requestedBy, DateTimeOffset now,
        CancellationToken token)
    {
        var requests = RequestsDirectory(configuration);
        if (!Directory.Exists(requests)) return InstallationUpdateRequestResult.TriggerMissing;
        if (!allowDowngrade && IsDowngrade(configuration["Installation:Version"], version))
            return InstallationUpdateRequestResult.Downgrade;
        var current = await ReadAsync(configuration, now, token);
        if (IsActive(current.Run) && !current.Stale) return InstallationUpdateRequestResult.InProgress;

        var target = Path.Combine(requests, "request.json");
        if (Directory.Exists(target)) return InstallationUpdateRequestResult.TriggerBlocked;
        if (File.Exists(target)) return InstallationUpdateRequestResult.InProgress;
        var temporary = Path.Combine(requests, $".request-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
            {
                version, allowDowngrade, requestedBy = SafeLabel(requestedBy), requestedAtUtc = now
            }), token);
            File.Move(temporary, target);
        }
        catch (IOException) when (File.Exists(target))
        {
            return InstallationUpdateRequestResult.InProgress;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return InstallationUpdateRequestResult.TriggerBlocked;
        }
        finally
        {
            try { File.Delete(temporary); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return InstallationUpdateRequestResult.Accepted;
    }

    // The host script accepts only letters, digits and a few separators in this label.
    internal static string SafeLabel(string value)
        => UnsafeCharacters().Replace(value, "_") is { Length: > 0 } label ? label[..Math.Min(label.Length, 200)] : "unknown";

    private static bool IsActive(InstallationUpdateRun? run) => run?.State is "starting" or "running";

    // Installation:Version holds the plain form (0.2.4); requests use the tag form (server-v0.2.4).
    internal static Version? Parse(string? tag)
    {
        if (tag is null) return null;
        var plain = tag.StartsWith("server-v", StringComparison.Ordinal) ? tag["server-v".Length..] : tag;
        return VersionPattern().IsMatch("server-v" + plain) && Version.TryParse(plain, out var version) ? version : null;
    }

    private static string RequestsDirectory(IConfiguration configuration)
        => configuration["Installation:UpdateRequestsDirectory"] ?? DefaultRequestsDirectory;

    private static string StatusDirectory(IConfiguration configuration)
        => configuration["Installation:UpdateStatusDirectory"] ?? DefaultStatusDirectory;

    private static async Task<string> ReadTailAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > MaxLogBytes) stream.Seek(-MaxLogBytes, SeekOrigin.End);
        var buffer = new byte[Math.Min(stream.Length, MaxLogBytes)];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), token);
            if (count == 0) break;
            read += count;
        }
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex(@"^server-v[0-9]+\.[0-9]+\.[0-9]+$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"[^A-Za-z0-9@._+ -]")]
    private static partial Regex UnsafeCharacters();
}
