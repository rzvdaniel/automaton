using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Api.Services;

public sealed record InstallationBackup(string Name, DateTimeOffset CreatedAt, long SizeBytes, string RestoreCommand);

public sealed record InstallationBackupStatus(
    bool Available,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    string? LastError,
    int Keep,
    string? Directory,
    string BackupNowCommand,
    IReadOnlyList<InstallationBackup> Backups);

/// <summary>
/// Reads the backup status that the host's backup.sh writes into the read-only installation status mount.
/// The API never runs backups or restores; it shows the commands an operator runs on the server.
/// </summary>
public static partial class InstallationBackups
{
    public const string DefaultStatusDirectory = "/app/installation-status";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(36);
    private const string InstallDirectory = "/opt/automaton-server";

    public static async Task<InstallationBackupStatus> ReadAsync(IConfiguration configuration, CancellationToken token)
    {
        var path = Path.Combine(configuration["Installation:StatusDirectory"] ?? DefaultStatusDirectory, "backup.json");
        var backupNow = $"sudo bash {InstallDirectory}/backup.sh";
        if (!File.Exists(path))
            return new(false, null, null, null, 0, null, backupNow, []);
        try
        {
            await using var stream = File.OpenRead(path);
            var file = await JsonSerializer.DeserializeAsync<StatusFile>(stream, JsonOptions, token)
                ?? throw new JsonException("Empty backup status.");
            var backups = (file.Backups ?? [])
                .Where(backup => backup.Name is not null && NamePattern().IsMatch(backup.Name))
                .Select(backup => new InstallationBackup(backup.Name!, backup.CreatedAt, backup.SizeBytes,
                    $"sudo bash {InstallDirectory}/restore.sh {backup.Name}"))
                .ToList();
            return new(true, file.LastAttemptAt, file.LastSuccessAt, file.LastError, file.Keep, file.Directory, backupNow, backups);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(true, null, null, "The backup status file could not be read.", 0, null, backupNow, []);
        }
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
        public string? LastError { get; init; }
        public int Keep { get; init; }
        public string? Directory { get; init; }
        public List<StatusBackup>? Backups { get; init; }
    }

    private sealed class StatusBackup
    {
        public string? Name { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public long SizeBytes { get; init; }
    }
}
