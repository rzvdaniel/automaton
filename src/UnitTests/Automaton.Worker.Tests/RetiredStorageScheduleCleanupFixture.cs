using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

internal sealed class RetiredStorageScheduleCleanupFixture : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly RetiredStorageScheduleCleanup seeder;

    private RetiredStorageScheduleCleanupFixture(ServiceProvider provider, RetiredStorageScheduleCleanup seeder)
    {
        this.provider = provider;
        this.seeder = seeder;
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
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schedules = await db.ActionSchedules
            .Where(schedule => schedule.ActionKey == key)
            .ToListAsync();
        var user = schedules.Single(schedule => schedule.OwnerType == ActionScheduleOwnerTypes.User);
        Assert.That(schedules.Any(schedule => schedule.OwnerType == ActionScheduleOwnerTypes.System), Is.False);
        Assert.That(user.Enabled, Is.True);
        Assert.That(user.NextRunAtUtc, Is.Not.Null);
        Assert.That(await db.ActionScheduleOccurrences.CountAsync(), Is.Zero);
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();
}
