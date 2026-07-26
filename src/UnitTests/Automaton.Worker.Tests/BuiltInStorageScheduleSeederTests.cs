using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

public class BuiltInStorageScheduleSeederTests
{
    [Test]
    public async Task StartAsync_IsIdempotentAndSeedsBothWorkerSchedules()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        var seeder = new BuiltInStorageScheduleSeeder(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BuiltInStorageScheduleSeeder>.Instance);

        await seeder.StartAsync(default);
        await seeder.StartAsync(default);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schedules = await db.ActionSchedules.OrderBy(x => x.ActionKey).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(schedules, Has.Count.EqualTo(3));
            Assert.That(schedules.Select(x => x.ActionKey), Is.EquivalentTo(new[]
            {
                BuiltInStorageScheduleActionKeys.HostedRunner,
                BuiltInStorageScheduleActionKeys.RunnerHost,
                BuiltInStorageScheduleActionKeys.Enforcement
            }));
            Assert.That(schedules, Has.All.Property(nameof(ActionSchedule.OwnerType)).EqualTo(ActionScheduleOwnerTypes.System));
            Assert.That(schedules, Has.All.Property(nameof(ActionSchedule.ExecutionScope)).EqualTo(ActionExecutionScopes.Worker));
            Assert.That(schedules, Has.All.Property(nameof(ActionSchedule.IntervalSeconds)).EqualTo(60));
        });
    }

    [Test]
    public async Task StartAsync_ReconcilesImmutableDefinitionWithoutDuplicatingSchedules()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        var seeder = new BuiltInStorageScheduleSeeder(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BuiltInStorageScheduleSeeder>.Instance);

        await seeder.StartAsync(default);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var schedule = await db.ActionSchedules.SingleAsync(x => x.ActionKey == BuiltInStorageScheduleActionKeys.HostedRunner);
            schedule.Name = "tampered";
            schedule.IntervalSeconds = 3600;
            schedule.ExecutionScope = ActionExecutionScopes.AppRunner;
            schedule.Enabled = false;
            schedule.NextRunAtUtc = null;
            await db.SaveChangesAsync();
        }

        await seeder.StartAsync(default);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reconciled = await verifyDb.ActionSchedules.SingleAsync(x => x.ActionKey == BuiltInStorageScheduleActionKeys.HostedRunner);
        Assert.Multiple(() =>
        {
            Assert.That(verifyDb.ActionSchedules.Count(), Is.EqualTo(3));
            Assert.That(reconciled.Name, Is.EqualTo("Hosted Runner storage reconciliation"));
            Assert.That(reconciled.IntervalSeconds, Is.EqualTo(60));
            Assert.That(reconciled.ExecutionScope, Is.EqualTo(ActionExecutionScopes.Worker));
            Assert.That(reconciled.Enabled, Is.True);
            Assert.That(reconciled.NextRunAtUtc, Is.Not.Null);
            Assert.That(reconciled.SystemIdentity, Is.EqualTo(BuiltInStorageScheduleActionKeys.HostedRunner));
        });
    }
}
