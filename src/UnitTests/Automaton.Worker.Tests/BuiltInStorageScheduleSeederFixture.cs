using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

internal sealed class BuiltInStorageScheduleSeederFixture : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly BuiltInStorageScheduleSeeder seeder;

    private BuiltInStorageScheduleSeederFixture(ServiceProvider provider, BuiltInStorageScheduleSeeder seeder)
    {
        this.provider = provider;
        this.seeder = seeder;
    }

    internal static Task<BuiltInStorageScheduleSeederFixture> CreateAsync()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        var provider = services.BuildServiceProvider();
        var seeder = new BuiltInStorageScheduleSeeder(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BuiltInStorageScheduleSeeder>.Instance);
        return Task.FromResult(new BuiltInStorageScheduleSeederFixture(provider, seeder));
    }

    internal async Task SeedOnceAsync() => await seeder.StartAsync(default);

    internal async Task SeedTwiceAsync()
    {
        await SeedOnceAsync();
        await SeedOnceAsync();
    }

    internal async Task TamperHostedRunnerScheduleAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schedule = await db.ActionSchedules.SingleAsync(x => x.ActionKey == BuiltInStorageScheduleActionKeys.HostedRunner);
        schedule.Name = "tampered";
        schedule.IntervalSeconds = 3600;
        schedule.ExecutionScope = ActionExecutionScopes.AppRunner;
        schedule.Enabled = false;
        schedule.NextRunAtUtc = null;
        await db.SaveChangesAsync();
    }

    internal async Task AssertSeededSchedulesAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schedules = await db.ActionSchedules.OrderBy(x => x.ActionKey).ToListAsync();
        AssertScheduleKeys(schedules);
        AssertScheduleDefaults(schedules);
    }

    private static void AssertScheduleKeys(List<ActionSchedule> schedules)
    {
        Assert.Multiple(() =>
        {
            Assert.That(schedules, Has.Count.EqualTo(3));
            Assert.That(schedules.Select(x => x.ActionKey), Is.EquivalentTo(new[]
            {
                BuiltInStorageScheduleActionKeys.HostedRunner,
                BuiltInStorageScheduleActionKeys.RunnerHost,
                BuiltInStorageScheduleActionKeys.Enforcement
            }));
        });
    }

    private static void AssertScheduleDefaults(List<ActionSchedule> schedules)
    {
        Assert.Multiple(() =>
        {
            Assert.That(schedules, Has.All.Property(nameof(ActionSchedule.OwnerType)).EqualTo(ActionScheduleOwnerTypes.System));
            Assert.That(schedules, Has.All.Property(nameof(ActionSchedule.ExecutionScope)).EqualTo(ActionExecutionScopes.Worker));
            Assert.That(schedules, Has.All.Property(nameof(ActionSchedule.IntervalSeconds)).EqualTo(60));
        });
    }

    internal async Task AssertHostedRunnerScheduleWasReconciledAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schedule = await db.ActionSchedules.SingleAsync(x => x.ActionKey == BuiltInStorageScheduleActionKeys.HostedRunner);
        Assert.Multiple(() =>
        {
            Assert.That(db.ActionSchedules.Count(), Is.EqualTo(3));
            Assert.That(schedule.Name, Is.EqualTo("Hosted Runner storage reconciliation"));
            Assert.That(schedule.IntervalSeconds, Is.EqualTo(60));
            Assert.That(schedule.ExecutionScope, Is.EqualTo(ActionExecutionScopes.Worker));
            Assert.That(schedule.Enabled, Is.True);
            Assert.That(schedule.NextRunAtUtc, Is.Not.Null);
            Assert.That(schedule.SystemIdentity, Is.EqualTo(BuiltInStorageScheduleActionKeys.HostedRunner));
        });
    }

    public ValueTask DisposeAsync() => provider.DisposeAsync();
}
