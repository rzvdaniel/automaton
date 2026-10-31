using Automaton.Api.Services;
using Microsoft.Extensions.Configuration;

namespace Automaton.Api.Tests;

public sealed class InstallationBackupsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ReadsBackupsAndBuildsRestoreCommandsForSafeNamesOnly()
    {
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "backup.json"), """
            {"lastAttemptAt":"2026-11-01T03:10:00Z","lastSuccessAt":"2026-11-01T03:10:00Z","lastError":null,"keep":7,
             "directory":"/opt/automaton-server/backups/nightly",
             "backups":[{"name":"automaton-20261101T031000Z","createdAt":"2026-11-01T03:10:00Z","sizeBytes":2048},
                        {"name":"automaton-x; rm -rf /","createdAt":"2026-11-01T03:10:00Z","sizeBytes":1}]}
            """);

        var status = await InstallationBackups.ReadAsync(Configuration(directory.Path), CancellationToken.None);

        Assert.That(status.Available, Is.True);
        Assert.That(status.Backups, Has.Count.EqualTo(1));
        Assert.That(status.Backups[0].RestoreCommand, Is.EqualTo("sudo bash /opt/automaton-server/restore.sh automaton-20261101T031000Z"));
        Assert.That(InstallationBackups.ToCheck(status, Now).Status, Is.EqualTo("configured"));
    }

    [Test]
    public async Task MissingStatusIsUnverified()
    {
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        var status = await InstallationBackups.ReadAsync(Configuration(directory.Path), CancellationToken.None);

        Assert.That(status.Available, Is.False);
        Assert.That(InstallationBackups.ToCheck(status, Now).Status, Is.EqualTo("unverified"));
    }

    [Test]
    public void FailedOrStaleBackupsAreErrors()
    {
        var failed = new InstallationBackupStatus(true, Now, Now.AddHours(-2), "The last backup failed.", 7, null, "", []);
        var stale = new InstallationBackupStatus(true, Now, Now.AddDays(-3), null, 7, null, "", []);
        var never = new InstallationBackupStatus(true, Now, null, null, 7, null, "", []);

        Assert.That(InstallationBackups.ToCheck(failed, Now).Status, Is.EqualTo("error"));
        Assert.That(InstallationBackups.ToCheck(stale, Now).Detail, Does.Contain("older than 36 hours"));
        Assert.That(InstallationBackups.ToCheck(never, Now).Status, Is.EqualTo("error"));
    }

    private static IConfiguration Configuration(string statusDirectory)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Installation:StatusDirectory"] = statusDirectory })
            .Build();
}
