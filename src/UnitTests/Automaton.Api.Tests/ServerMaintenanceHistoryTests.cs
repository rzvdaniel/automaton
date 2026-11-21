using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ServerMaintenanceHistoryTests
{
    private string _root = null!;
    private ApplicationDbContext _db = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "history-" + Guid.NewGuid().ToString("N"));
        foreach (var name in new[] { "os", "restart", "status" }) Directory.CreateDirectory(Path.Combine(_root, name));
        _db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        Directory.Delete(_root, true);
    }

    private ServerMaintenanceHistory History() => new(_db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Installation:OsUpdateStatusDirectory"] = Path.Combine(_root, "os"),
        ["Installation:OsUpdateRequestsDirectory"] = Path.Combine(_root, "os-requests"),
        ["Installation:RestartStatusDirectory"] = Path.Combine(_root, "restart"),
        ["Installation:BootTimeFile"] = Path.Combine(_root, "stat"),
        ["Installation:RestartRequestsDirectory"] = Path.Combine(_root, "restart-requests"),
        ["Installation:StatusDirectory"] = Path.Combine(_root, "status"),
        ["Installation:BackupRequestsDirectory"] = Path.Combine(_root, "backup-requests")
    }).Build(), null!);

    [Test]
    public async Task A_finished_OS_update_is_kept_with_who_started_it_and_the_package_count()
    {
        File.WriteAllText(Path.Combine(_root, "os", "status.json"), """
            {"state":"succeeded","scope":"security","requestedBy":"admin@example.com","requestedAtUtc":"2026-11-21T10:00:00Z",
             "finishedAtUtc":"2026-11-21T10:04:00Z","upgradedCount":9}
            """);
        var runs = await History().ListAsync(DateTimeOffset.Parse("2026-11-21T11:00:00Z"), default);
        var run = runs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.Kind, Is.EqualTo("os-update"));
            Assert.That(run.State, Is.EqualTo("succeeded"));
            Assert.That(run.RequestedBy, Is.EqualTo("admin@example.com"));
            Assert.That(run.UpgradedCount, Is.EqualTo(9));
            Assert.That(run.Message, Does.Contain("9 packages"));
        });
    }

    [Test]
    public async Task Reading_again_updates_the_same_run_and_a_new_run_is_added()
    {
        var path = Path.Combine(_root, "os", "status.json");
        File.WriteAllText(path, """{"state":"running","requestedBy":"a","requestedAtUtc":"2026-11-21T10:00:00Z"}""");
        var now = DateTimeOffset.Parse("2026-11-21T10:01:00Z");
        Assert.That((await History().ListAsync(now, default)).Single().State, Is.EqualTo("running"));
        File.WriteAllText(path, """{"state":"failed","requestedBy":"a","requestedAtUtc":"2026-11-21T10:00:00Z","finishedAtUtc":"2026-11-21T10:02:00Z","message":"apt failed"}""");
        var runs = await History().ListAsync(now, default);
        Assert.That(runs.Single().State, Is.EqualTo("failed"));
        Assert.That(runs.Single().Message, Is.EqualTo("apt failed"));
        File.WriteAllText(path, """{"state":"succeeded","requestedBy":"b","requestedAtUtc":"2026-11-21T12:00:00Z","finishedAtUtc":"2026-11-21T12:01:00Z","upgradedCount":2}""");
        runs = await History().ListAsync(now, default);
        Assert.That(runs.Select(run => run.RequestedBy), Is.EqualTo(new[] { "b", "a" }));
    }

    [Test]
    public async Task A_server_restart_is_listed_as_succeeded_from_the_boot_time()
    {
        File.WriteAllText(Path.Combine(_root, "stat"), "cpu 1 2 3\nbtime 1795000000\n");
        var run = (await History().ListAsync(DateTimeOffset.UtcNow, default)).Single();
        Assert.That((run.Kind, run.State), Is.EqualTo(("restart", "succeeded")));
        Assert.That(run.StartedAtUtc, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1795000000).UtcDateTime));
    }

    [Test]
    public async Task A_backup_that_failed_is_listed_with_its_error()
    {
        File.WriteAllText(Path.Combine(_root, "status", "backup.json"), """
            {"lastAttemptAt":"2026-11-21T03:00:00Z","lastSuccessAt":"2026-11-20T03:00:00Z","lastError":"disk full","keep":7,"backups":[]}
            """);
        var run = (await History().ListAsync(DateTimeOffset.UtcNow, default)).Single();
        Assert.That(run.Kind, Is.EqualTo("backup"));
        Assert.That(run.State, Is.EqualTo("failed"));
        Assert.That(run.Message, Is.EqualTo("disk full"));
    }

    [Test]
    public async Task A_backup_that_completed_is_listed_as_succeeded()
    {
        File.WriteAllText(Path.Combine(_root, "status", "backup.json"), """
            {"lastAttemptAt":"2026-11-21T03:00:00Z","lastSuccessAt":"2026-11-21T03:02:00Z","keep":7,"backups":[]}
            """);
        Assert.That((await History().ListAsync(DateTimeOffset.UtcNow, default)).Single().State, Is.EqualTo("succeeded"));
    }

    [Test]
    public async Task The_list_returns_the_newest_twenty_and_nothing_when_the_host_reports_nothing()
    {
        Assert.That(await History().ListAsync(DateTimeOffset.UtcNow, default), Is.Empty);
        for (var index = 0; index < 25; index++)
            _db.ServerMaintenanceRuns.Add(new ServerMaintenanceRun
            {
                Id = Guid.NewGuid(), Kind = "backup", StartedAtUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index),
                State = "succeeded"
            });
        await _db.SaveChangesAsync();
        var runs = await History().ListAsync(DateTimeOffset.UtcNow, default);
        Assert.That(runs, Has.Count.EqualTo(20));
        Assert.That(runs[0].StartedAtUtc, Is.GreaterThan(runs[19].StartedAtUtc));
    }
}
