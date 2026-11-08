using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Api.Services;

public sealed record InstallationBackup(string Name, DateTimeOffset CreatedAt, long SizeBytes, string RestoreCommand,
    long? ArchiveSizeBytes, string? ArchiveSha256);

public sealed record InstallationBackupStatus(
    bool Available,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    string? LastError,
    int Keep,
    string? Directory,
    string BackupNowCommand,
    string? BackupRecipient,
    string BackupKeyCommand,
    IReadOnlyList<InstallationBackup> Backups,
    DateTimeOffset? NextRunAt = null,
    bool TriggerAvailable = false,
    bool RequestPending = false);

public enum InstallationBackupRequestResult { Accepted, InProgress, TriggerMissing, TriggerBlocked }

/// <summary>
/// Reads the backup status that the host's backup.sh writes into the read-only installation status mount.
/// The API never runs backups or restores itself; restores are commands an operator runs on the server.
/// </summary>
public static partial class InstallationBackups
{
    public const string DefaultStatusDirectory = "/app/installation-status";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(36);
    private const string InstallDirectory = "/opt/automaton-server";
    public const string DefaultBackupDirectory = "/app/installation-backups";
    public const string DefaultRequestsDirectory = "/app/backup-requests";

    public static async Task<InstallationBackupStatus> ReadAsync(IConfiguration configuration, CancellationToken token)
    {
        var status = await ReadStatusFileAsync(configuration, token);
        var requests = RequestsDirectory(configuration);
        return status with
        {
            TriggerAvailable = Directory.Exists(requests),
            RequestPending = File.Exists(Path.Combine(requests, "request.json"))
        };
    }

    /// <summary>
    /// Asks the host to take a backup now. Like server updates, the API never runs it: it drops a request file that a
    /// root systemd path unit (automaton-backup-request.path, installed by backup.sh) turns into the normal backup.
    /// </summary>
    public static async Task<InstallationBackupRequestResult> RequestAsync(
        IConfiguration configuration, string requestedBy, DateTimeOffset now, CancellationToken token)
    {
        var requests = RequestsDirectory(configuration);
        if (!Directory.Exists(requests)) return InstallationBackupRequestResult.TriggerMissing;
        var target = Path.Combine(requests, "request.json");
        if (Directory.Exists(target)) return InstallationBackupRequestResult.TriggerBlocked;
        if (File.Exists(target)) return InstallationBackupRequestResult.InProgress;
        var temporary = Path.Combine(requests, $".request-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
            {
                requestedBy = InstallationUpdate.SafeLabel(requestedBy), requestedAtUtc = now
            }), token);
            File.Move(temporary, target);
            return InstallationBackupRequestResult.Accepted;
        }
        catch (IOException) when (File.Exists(target)) { return InstallationBackupRequestResult.InProgress; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return InstallationBackupRequestResult.TriggerBlocked;
        }
        finally
        {
            try { File.Delete(temporary); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string RequestsDirectory(IConfiguration configuration)
        => configuration["Installation:BackupRequestsDirectory"] ?? DefaultRequestsDirectory;

    private static async Task<InstallationBackupStatus> ReadStatusFileAsync(IConfiguration configuration, CancellationToken token)
    {
        var path = Path.Combine(configuration["Installation:StatusDirectory"] ?? DefaultStatusDirectory, "backup.json");
        var backupNow = $"sudo bash {InstallDirectory}/backup.sh";
        const string backupKeyCommand = "sudo cat /etc/automaton/secrets/backup-key";
        if (!File.Exists(path))
            return new(false, null, null, null, 0, null, backupNow, null, backupKeyCommand, []);
        try
        {
            await using var stream = File.OpenRead(path);
            var file = await JsonSerializer.DeserializeAsync<StatusFile>(stream, JsonOptions, token)
                ?? throw new JsonException("Empty backup status.");
            var backups = (file.Backups ?? [])
                .Where(backup => backup.Name is not null && NamePattern().IsMatch(backup.Name))
                .Select(backup => new InstallationBackup(backup.Name!, backup.CreatedAt, backup.SizeBytes,
                    $"sudo bash {InstallDirectory}/restore.sh {backup.Name}", backup.ArchiveSizeBytes, backup.ArchiveSha256))
                .ToList();
            return new(true, file.LastAttemptAt, file.LastSuccessAt, file.LastError, file.Keep, file.Directory, backupNow, file.BackupRecipient, file.BackupKeyCommand ?? backupKeyCommand, backups, file.NextRunAt);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(true, null, null, "The backup status file could not be read.", 0, null, backupNow, null, backupKeyCommand, []);
        }
    }

    public static string? GetArchivePath(IConfiguration configuration, InstallationBackupStatus status, string name)
    {
        if (!NamePattern().IsMatch(name) || !status.Backups.Any(backup => backup.Name == name)) return null;
        var path = Path.Combine(configuration["Installation:BackupDirectory"] ?? DefaultBackupDirectory, name + ".tar.age");
        var file = new FileInfo(path);
        return file.Exists && file.LinkTarget is null ? path : null;
    }

    public static InstallationCheck ToCheck(InstallationBackupStatus status, DateTimeOffset now)
    {
        if (!status.Available)
            return new("Backups", "unverified",
                "No backup status found. Run the latest server update to enable nightly backups, and turn on your provider's server backups.");
        if (status.LastSuccessAt is not { } last)
            return new("Backups", "error", status.LastError ?? "No backup has completed yet.");
        var detail = $"Last nightly backup {last:u}; {status.Backups.Count} kept on the server. Also turn on your provider's server backups and copy backups off-host.";
        if (status.LastError is not null) return new("Backups", "error", $"{status.LastError} {detail}");
        return now - last > StaleAfter
            ? new("Backups", "error", $"The last backup is older than {StaleAfter.TotalHours:0} hours. {detail}")
            : new("Backups", "configured", detail);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex(@"^automaton-[0-9]{8}T[0-9]{6}Z$")]
    private static partial Regex NamePattern();

    private sealed class StatusFile
    {
        public DateTimeOffset? LastAttemptAt { get; init; }
        public DateTimeOffset? LastSuccessAt { get; init; }
        public DateTimeOffset? NextRunAt { get; init; }
        public string? LastError { get; init; }
        public int Keep { get; init; }
        public string? Directory { get; init; }
        public string? BackupRecipient { get; init; }
        public string? BackupKeyCommand { get; init; }
        public List<StatusBackup>? Backups { get; init; }
    }

    private sealed class StatusBackup
    {
        public string? Name { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public long SizeBytes { get; init; }
        public long? ArchiveSizeBytes { get; init; }
        public string? ArchiveSha256 { get; init; }
    }
}
