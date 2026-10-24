using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Automaton.Worker.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

internal sealed class RetiredStorageScheduleCleanupFixture : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly RetiredStorageScheduleCleanup seeder;
    private readonly SqliteConnection? connection;

    private RetiredStorageScheduleCleanupFixture(ServiceProvider provider, RetiredStorageScheduleCleanup seeder, SqliteConnection? connection = null)
    {
        this.provider = provider;
        this.seeder = seeder;
        this.connection = connection;
    }

    internal static Task<RetiredStorageScheduleCleanupFixture> CreateAsync()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        var provider = services.BuildServiceProvider();
        var seeder = new RetiredStorageScheduleCleanup(
            provider.GetRequiredService<IServiceScopeFactory>());
        return Task.FromResult(new RetiredStorageScheduleCleanupFixture(provider, seeder));
    }

    internal static async Task<RetiredStorageScheduleCleanupFixture> CreateRelationalAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE ActionSchedules (
                    Id TEXT PRIMARY KEY, OwnerType TEXT NOT NULL, SystemIdentity TEXT NULL,
                    ApplicationId TEXT NOT NULL, ActionKey TEXT NOT NULL, Enabled INTEGER NOT NULL,
                    NextRunAtUtc TEXT NULL, ConcurrencyPolicy TEXT NOT NULL, MisfirePolicy TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL, CronExpression TEXT NULL, ScheduleType TEXT NULL,
                    Name TEXT NULL, Description TEXT NULL, ExecutionScope TEXT NULL, TimeZone TEXT NULL, InputJson TEXT NULL,
                    RunAtUtc TEXT NULL, IntervalSeconds INTEGER NULL, LastTriggeredAtUtc TEXT NULL, LastExecutionId TEXT NULL,
                    OwnerUserId TEXT NULL, LeaseOwner TEXT NULL, LeaseExpiresAtUtc TEXT NULL);
                CREATE TABLE ActionScheduleOccurrences (
                    Id TEXT PRIMARY KEY, ScheduleId TEXT NOT NULL, Status TEXT NOT NULL);
                CREATE TABLE AppModuleExecutions (
                    Id TEXT PRIMARY KEY, ScheduleOccurrenceId TEXT NULL, Status TEXT NOT NULL);
                """);
        }
        var seeder = new RetiredStorageScheduleCleanup(provider.GetRequiredService<IServiceScopeFactory>());
        return new RetiredStorageScheduleCleanupFixture(provider, seeder, connection);
    }

    internal async Task SeedRelationalAsync(string retiredKey)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO ActionSchedules (Id, OwnerType, SystemIdentity, ApplicationId, ActionKey, Enabled, NextRunAtUtc, ConcurrencyPolicy, MisfirePolicy, CreatedAtUtc, UpdatedAtUtc)
            VALUES ({2}, {7}, {0}, {1}, {0}, 1, '2030-01-01', 'SkipIfRunning', 'Skip', '2030-01-01', '2030-01-01');
            INSERT INTO ActionSchedules (Id, OwnerType, SystemIdentity, ApplicationId, ActionKey, Enabled, NextRunAtUtc, ConcurrencyPolicy, MisfirePolicy, CreatedAtUtc, UpdatedAtUtc)
            VALUES ({3}, {8}, NULL, {1}, {0}, 1, '2030-01-01', 'SkipIfRunning', 'Skip', '2030-01-01', '2030-01-01');
            INSERT INTO ActionScheduleOccurrences (Id, ScheduleId, Status) VALUES ({4}, {2}, {9});
            INSERT INTO AppModuleExecutions (Id, ScheduleOccurrenceId, Status) VALUES ({5}, {4}, {10});
            INSERT INTO ActionScheduleOccurrences (Id, ScheduleId, Status) VALUES ({6}, {2}, {11});
            """, retiredKey, InfrastructureAppTrust.PlatformAppId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ActionScheduleOwnerTypes.System, ActionScheduleOwnerTypes.User, ActionScheduleOccurrenceStatuses.Dispatched, AppModuleExecutionStatuses.Succeeded, ActionScheduleOccurrenceStatuses.Queued);
    }

    internal async Task AssertRelationalCleanupAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schedules = db.ActionSchedules.AsNoTracking();
        Assert.That(await schedules.CountAsync(schedule => schedule.OwnerType == ActionScheduleOwnerTypes.System && schedule.SystemIdentity != null), Is.EqualTo(1));
        Assert.That(await schedules.Where(schedule => schedule.OwnerType == ActionScheduleOwnerTypes.System && schedule.SystemIdentity != null)
            .Select(schedule => new { schedule.Enabled, schedule.NextRunAtUtc })
            .AnyAsync(schedule => schedule.Enabled || schedule.NextRunAtUtc != null), Is.False);
        Assert.That(await schedules.Where(schedule => schedule.OwnerType == ActionScheduleOwnerTypes.User)
            .Select(schedule => schedule.Enabled).SingleAsync(), Is.True);
        Assert.That(await db.ActionScheduleOccurrences.AsNoTracking().CountAsync(occurrence => occurrence.Status == ActionScheduleOccurrenceStatuses.Dispatched), Is.Zero);
        Assert.That(await db.AppModuleExecutions.AsNoTracking().CountAsync(), Is.Zero);
        Assert.That(await db.ActionScheduleOccurrences.AsNoTracking().CountAsync(occurrence => occurrence.Status == ActionScheduleOccurrenceStatuses.Queued), Is.EqualTo(1));
    }

    internal async Task SeedOnceAsync() => await seeder.StartAsync(default);

    internal async Task SeedTwiceAsync()
    {
        await SeedOnceAsync();
        await SeedOnceAsync();
    }

    internal async Task SeedRetiredScheduleAsync(string key, bool legacyIdentity)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.WebApps.Add(new WebApp
        {
            Id = InfrastructureAppTrust.PlatformAppId,
            Name = "Automaton platform",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = string.Empty
        });
        var systemSchedule = new ActionSchedule
        {
            Id = Guid.NewGuid(), OwnerType = ActionScheduleOwnerTypes.System,
            ApplicationId = InfrastructureAppTrust.PlatformAppId,
            SystemIdentity = !legacyIdentity ? key : null,
            ActionKey = key, Enabled = true, NextRunAtUtc = DateTime.UtcNow.AddMinutes(1)
        };
        db.ActionSchedules.Add(systemSchedule);
        db.ActionSchedules.Add(new ActionSchedule
        {
            Id = Guid.NewGuid(), OwnerType = ActionScheduleOwnerTypes.User,
            ApplicationId = InfrastructureAppTrust.PlatformAppId, ActionKey = key,
            Enabled = true, NextRunAtUtc = DateTime.UtcNow.AddMinutes(1)
        });
        await db.SaveChangesAsync();

        var occurrence = new ActionScheduleOccurrence
        {
            Id = Guid.NewGuid(), ScheduleId = systemSchedule.Id, Schedule = systemSchedule,
            ScheduledForUtc = DateTime.UtcNow, TriggeredAtUtc = DateTime.UtcNow,
            IdempotencyKey = Guid.NewGuid().ToString("N"), Status = ActionScheduleOccurrenceStatuses.Dispatched,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        db.ActionScheduleOccurrences.Add(occurrence);
        db.AppModuleExecutions.Add(new AppModuleExecution
        {
            Id = Guid.NewGuid(), ScheduleOccurrenceId = occurrence.Id,
            AppId = InfrastructureAppTrust.PlatformAppId, ModuleType = "Action", ModuleKey = key,
            TriggerType = "Scheduled", Status = "Succeeded", CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    internal async Task AssertRetiredScheduleDeletedAsync(string key)
        => await RetiredStorageScheduleCleanupAssertions.AssertDeletedAsync(provider, key);

    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        if (connection is not null) await connection.DisposeAsync();
    }
}

internal static class RetiredStorageScheduleCleanupAssertions
{
    internal static async Task AssertDeletedAsync(ServiceProvider provider, string key)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AssertSchedulesDeletedAsync(db, key);
        Assert.That(await db.ActionScheduleOccurrences.CountAsync(), Is.Zero);
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
    }

    private static async Task AssertSchedulesDeletedAsync(ApplicationDbContext db, string key)
    {
        var schedules = db.ActionSchedules.Where(schedule => schedule.ActionKey == key);
        Assert.That(await schedules.CountAsync(schedule => schedule.OwnerType == ActionScheduleOwnerTypes.System), Is.Zero);
        var user = await schedules.Where(schedule => schedule.OwnerType == ActionScheduleOwnerTypes.User)
            .Select(schedule => new { schedule.Enabled, schedule.NextRunAtUtc }).SingleAsync();
        Assert.That(user.Enabled, Is.True);
        Assert.That(user.NextRunAtUtc, Is.Not.Null);
    }
}
