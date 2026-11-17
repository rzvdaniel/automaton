using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Retention;
using Automaton.Core.Models;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class RetentionPrunerTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 4, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static AppModuleExecution Run(Guid app, string key, int ageDays, string status = AppModuleExecutionStatuses.Succeeded)
        => new() { Id = Guid.NewGuid(), AppId = app, ModuleKey = key, ModuleType = "Action", TriggerType = "Schedule",
            Status = status, CreatedAtUtc = Now.AddDays(-ageDays), CompletedAtUtc = Now.AddDays(-ageDays),
            LogsAvailableUntilUtc = Now.AddDays(-ageDays + 30) };

    [Test]
    public async Task Old_successes_go_but_last_twenty_and_failures_stay()
    {
        await using var db = NewDb();
        var app = Guid.NewGuid();
        db.AppModuleExecutions.AddRange(Enumerable.Range(0, 25).Select(i => Run(app, "sync", 40 + i)));
        var recentFailure = Run(app, "sync", 50, AppModuleExecutionStatuses.Failed); // under 2x30 days
        var oldFailure = Run(app, "sync", 70, AppModuleExecutionStatuses.Failed);
        db.AppModuleExecutions.AddRange(recentFailure, oldFailure);
        await db.SaveChangesAsync();

        var report = await new RetentionPruner(db).PruneAsync(Now, default);

        var left = await db.AppModuleExecutions.Select(x => x.Id).ToListAsync();
        Assert.That(left, Does.Contain(recentFailure.Id));
        Assert.That(left, Does.Not.Contain(oldFailure.Id));
        // 25 old successes minus the 20 newest of the Action overall (including the failures in the ranking).
        Assert.That(report.Runs, Is.GreaterThan(0));
        Assert.That(left.Count, Is.GreaterThanOrEqualTo(20));
        Assert.That(await db.AppModuleExecutions.MaxAsync(x => x.CreatedAtUtc), Is.EqualTo(Now.AddDays(-40)));
    }

    [Test]
    public async Task App_override_wins_over_default()
    {
        await using var db = NewDb();
        var app = Guid.NewGuid();
        db.AppModuleExecutions.AddRange(Enumerable.Range(0, 5).Select(i => Run(app, "sync", 10 + i)));
        await db.SaveChangesAsync();
        await new RetentionPruner(db).PruneAsync(Now, default);
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.EqualTo(5), "default keeps them");

        await RetentionSettings.SaveAppOverrideAsync(db, app, new RetentionAppOverride { RunDays = 1, MinimumRunsPerAction = 2 }, false, default);
        var report = await new RetentionPruner(db).PruneAsync(Now, default);

        Assert.That(report.Runs, Is.EqualTo(3));
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Home_card_keeps_last_three_plus_failures()
    {
        await using var db = NewDb();
        var app = Guid.NewGuid();
        db.AppModuleExecutions.AddRange(Enumerable.Range(0, 6).Select(i => Run(app, "home-card", i)));
        var failure = Run(app, "home-card", 8, AppModuleExecutionStatuses.Failed);
        db.AppModuleExecutions.Add(failure);
        await db.SaveChangesAsync();

        await new RetentionPruner(db).PruneAsync(Now, default);

        var left = await db.AppModuleExecutions.Where(x => x.Status == AppModuleExecutionStatuses.Succeeded).CountAsync();
        Assert.That(left, Is.EqualTo(3));
        Assert.That(await db.AppModuleExecutions.AnyAsync(x => x.Id == failure.Id), Is.True);
    }

    [Test]
    public async Task Running_runs_and_the_schedules_last_run_are_never_removed()
    {
        await using var db = NewDb();
        var app = Guid.NewGuid();
        var running = Run(app, "sync", 400, AppModuleExecutionStatuses.Running);
        var last = Run(app, "sync", 399);
        db.AppModuleExecutions.AddRange(running, last);
        db.ActionSchedules.Add(new ActionSchedule { Id = Guid.NewGuid(), ApplicationId = app, ActionKey = "sync", LastExecutionId = last.Id });
        await RetentionSettings.SaveAppOverrideAsync(db, app, new RetentionAppOverride { RunDays = 1, MinimumRunsPerAction = 0 }, false, default);
        await db.SaveChangesAsync();

        await new RetentionPruner(db).PruneAsync(Now, default);

        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Logs_expire_after_log_days_but_run_stays()
    {
        await using var db = NewDb();
        var app = Guid.NewGuid();
        var run = Run(app, "sync", 10);
        var fresh = Run(app, "sync", 2);
        db.AppModuleExecutions.AddRange(run, fresh);
        await db.SaveChangesAsync();

        var report = await new RetentionPruner(db).PruneAsync(Now, default);

        Assert.That(report.LogsExpired, Is.EqualTo(1));
        Assert.That((await db.AppModuleExecutions.SingleAsync(x => x.Id == run.Id)).LogsAvailableUntilUtc, Is.EqualTo(Now));
        Assert.That((await db.AppModuleExecutions.SingleAsync(x => x.Id == fresh.Id)).LogsAvailableUntilUtc, Is.GreaterThan(Now));
    }

    [Test]
    public async Task Old_audit_events_go_but_recent_and_secret_events_stay()
    {
        await using var db = NewDb();
        AuditEvent Event(string entity, int ageDays) => new() { Id = Guid.NewGuid(), EntityType = entity, CreatedAtUtc = Now.AddDays(-ageDays) };
        var old = Event("User", 400);
        var recent = Event("User", 10);
        var secret = Event("AppSecretVault", 400);
        db.AuditEvents.AddRange(old, recent, secret);
        await db.SaveChangesAsync();
        await RetentionSettings.SaveDefaultsAsync(db, new RetentionDefaults { AuditDays = 30 }, false, default);

        var report = await new RetentionPruner(db).PruneAsync(Now, default);

        Assert.That(report.AuditEvents, Is.EqualTo(1));
        var left = await db.AuditEvents.Select(x => x.Id).ToListAsync();
        Assert.That(left, Is.EquivalentTo(new[] { recent.Id, secret.Id }));
    }

    [Test]
    public async Task Long_retention_needs_approval()
    {
        await using var db = NewDb();
        Assert.ThrowsAsync<ArgumentException>(() => RetentionSettings.SaveDefaultsAsync(db, new RetentionDefaults { RunDays = 120 }, false, default));
        await RetentionSettings.SaveDefaultsAsync(db, new RetentionDefaults { RunDays = 120 }, true, default);
        Assert.That((await RetentionSettings.GetDefaultsAsync(db, default)).RunDays, Is.EqualTo(120));
        Assert.ThrowsAsync<ArgumentException>(() => RetentionSettings.SaveAppOverrideAsync(db, Guid.NewGuid(), new RetentionAppOverride { LogDays = 91 }, false, default));
    }

    [Test]
    public void Job_runs_at_four_utc()
    {
        Assert.That(RetentionPruneService.DelayUntilNextRun(new DateTime(2026, 11, 16, 3, 0, 0, DateTimeKind.Utc)), Is.EqualTo(TimeSpan.FromHours(1)));
        Assert.That(RetentionPruneService.DelayUntilNextRun(new DateTime(2026, 11, 16, 4, 0, 0, DateTimeKind.Utc)), Is.EqualTo(TimeSpan.FromHours(24)));
    }
}
