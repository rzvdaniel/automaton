using Automaton.Api.Services;
using Automaton.Api.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;

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
             "backupRecipient":"age1recipient","backups":[{"name":"automaton-20261101T031000Z","createdAt":"2026-11-01T03:10:00Z","sizeBytes":2048,"archiveSizeBytes":4096,"archiveSha256":"abcd"},
                        {"name":"automaton-x; rm -rf /","createdAt":"2026-11-01T03:10:00Z","sizeBytes":1}]}
            """);

        var status = await InstallationBackups.ReadAsync(Configuration(directory.Path), CancellationToken.None);

        Assert.That(status.Available, Is.True);
        Assert.That(status.Backups, Has.Count.EqualTo(1));
        Assert.That(status.Backups[0].RestoreCommand, Is.EqualTo("sudo bash /opt/automaton-server/restore.sh automaton-20261101T031000Z"));
        Assert.That(status.BackupRecipient, Is.EqualTo("age1recipient"));
        Assert.That(InstallationBackups.ToCheck(status, Now).Status, Is.EqualTo("configured"));
    }

    [Test]
    public async Task ArchivePathRequiresAListedSafeNameAndRejectsSymlinks()
    {
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        var name = "automaton-20261101T031000Z";
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "backup.json"), $$"""
            {"backups":[{"name":"{{name}}","createdAt":"2026-11-01T03:10:00Z","sizeBytes":1}]}
            """);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Installation:StatusDirectory"] = directory.Path,
            ["Installation:BackupDirectory"] = directory.Path
        }).Build();
        var status = await InstallationBackups.ReadAsync(config, CancellationToken.None);
        var archive = Path.Combine(directory.Path, name + ".tar.age");
        await File.WriteAllBytesAsync(archive, [1, 2]);

        Assert.That(InstallationBackups.GetArchivePath(config, status, name), Is.EqualTo(archive));
        Assert.That(InstallationBackups.GetArchivePath(config, status, "../backup.json"), Is.Null);
        Assert.That(InstallationBackups.GetArchivePath(config, status, "automaton-20261102T031000Z"), Is.Null);

        var link = Path.Combine(directory.Path, "automaton-20261101T031001Z.tar.age");
        try { File.CreateSymbolicLink(link, Path.Combine(directory.Path, "backup.json")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return; }
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "backup.json"), $$"""
            {"backups":[{"name":"{{name}}","createdAt":"2026-11-01T03:10:00Z","sizeBytes":1},
                         {"name":"automaton-20261101T031001Z","createdAt":"2026-11-01T03:10:01Z","sizeBytes":1}]}
            """);
        status = await InstallationBackups.ReadAsync(config, CancellationToken.None);
        Assert.That(InstallationBackups.GetArchivePath(config, status, "automaton-20261101T031001Z"), Is.Null);
    }

    [Test]
    public void ArchiveDownloadUsesTheSameScopedPermissionAsTheBackupList()
    {
        var list = typeof(InstallController).GetMethod(nameof(InstallController.GetBackups))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        var archive = typeof(InstallController).GetMethod(nameof(InstallController.GetBackupArchive))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();

        Assert.That(archive.Policy, Is.EqualTo(list.Policy));
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
        var failed = new InstallationBackupStatus(true, Now, Now.AddHours(-2), "The last backup failed.", 7, null, "", null, "", []);
        var stale = new InstallationBackupStatus(true, Now, Now.AddDays(-3), null, 7, null, "", null, "", []);
        var never = new InstallationBackupStatus(true, Now, null, null, 7, null, "", null, "", []);

        Assert.That(InstallationBackups.ToCheck(failed, Now).Status, Is.EqualTo("error"));
        Assert.That(InstallationBackups.ToCheck(stale, Now).Detail, Does.Contain("older than 36 hours"));
        Assert.That(InstallationBackups.ToCheck(never, Now).Status, Is.EqualTo("error"));
    }

    [Test]
    public async Task ReportsTheNextScheduledRunAndWhetherARequestIsPending()
    {
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        using var requests = new AppAuthorizationTestData.TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "backup.json"),
            """{"lastSuccessAt":"2026-11-01T03:10:00Z","nextRunAt":"2026-11-02T03:25:00Z","keep":7,"backups":[]}""");
        var config = RequestConfiguration(directory.Path, requests.Path);

        var idle = await InstallationBackups.ReadAsync(config, CancellationToken.None);
        Assert.That(idle.NextRunAt, Is.EqualTo(new DateTimeOffset(2026, 11, 2, 3, 25, 0, TimeSpan.Zero)));
        Assert.That(idle.TriggerAvailable, Is.True);
        Assert.That(idle.RequestPending, Is.False);

        await InstallationBackups.RequestAsync(config, "admin@example.com", Now, CancellationToken.None);
        Assert.That((await InstallationBackups.ReadAsync(config, CancellationToken.None)).RequestPending, Is.True);
    }

    [Test]
    public async Task RequestWritesOneFileAndRefusesASecondUntilTheHostTakesIt()
    {
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        using var requests = new AppAuthorizationTestData.TemporaryDirectory();
        var config = RequestConfiguration(directory.Path, requests.Path);

        Assert.That(await InstallationBackups.RequestAsync(config, "admin@example.com", Now, CancellationToken.None), Is.EqualTo(InstallationBackupRequestResult.Accepted));
        Assert.That(await InstallationBackups.RequestAsync(config, "x", Now, CancellationToken.None), Is.EqualTo(InstallationBackupRequestResult.InProgress));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(requests.Path, "request.json")), Does.Contain("admin@example.com"));
        Assert.That(Directory.GetFiles(requests.Path), Has.Length.EqualTo(1));

        File.Delete(Path.Combine(requests.Path, "request.json"));
        Directory.CreateDirectory(Path.Combine(requests.Path, "request.json"));
        Assert.That(await InstallationBackups.RequestAsync(config, "x", Now, CancellationToken.None), Is.EqualTo(InstallationBackupRequestResult.TriggerBlocked));
    }

    [Test]
    public async Task RequestWithoutTheHostTriggerIsReported()
    {
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        var config = RequestConfiguration(directory.Path, Path.Combine(directory.Path, "missing"));

        Assert.That(await InstallationBackups.RequestAsync(config, "x", Now, CancellationToken.None), Is.EqualTo(InstallationBackupRequestResult.TriggerMissing));
        Assert.That((await InstallationBackups.ReadAsync(config, CancellationToken.None)).TriggerAvailable, Is.False);
    }

    [Test]
    public void StartingABackupIsOnlyForSystemAdministrators()
    {
        var authorize = typeof(InstallController).GetMethod(nameof(InstallController.StartBackup))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();

        Assert.That(authorize.Roles, Is.EqualTo(Automaton.Api.Enums.UserRole.SystemAdministrator));
        Assert.That(authorize.Policy, Is.EqualTo("DeveloperApiAccess"));
    }

    private static IConfiguration RequestConfiguration(string statusDirectory, string requestsDirectory)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Installation:StatusDirectory"] = statusDirectory,
            ["Installation:BackupRequestsDirectory"] = requestsDirectory
        }).Build();

    private static IConfiguration Configuration(string statusDirectory)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Installation:StatusDirectory"] = statusDirectory })
            .Build();
}
