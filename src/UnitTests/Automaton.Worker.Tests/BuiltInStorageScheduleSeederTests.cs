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
            Assert.That(schedules, Has.Count.EqualTo(2));
            Assert.That(schedules.Select(x => x.ActionKey), Is.EquivalentTo(new[]
            {
                BuiltInStorageScheduleActionKeys.HostedRunner,
                BuiltInStorageScheduleActionKeys.RunnerHost
            }));
            Assert.That(schedules, Has.All.Property(nameof(ActionSchedule.OwnerType)).EqualTo(ActionScheduleOwnerTypes.System));
            Assert.That(schedules, Has.All.Property(nameof(ActionSchedule.ExecutionScope)).EqualTo(ActionExecutionScopes.Worker));
            Assert.That(schedules, Has.All.Property(nameof(ActionSchedule.IntervalSeconds)).EqualTo(60));
        });
    }
}
