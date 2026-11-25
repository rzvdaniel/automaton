using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.Extensions.Configuration;

namespace Automaton.Api.Tests;

public sealed class PlannedRunnerRestartReasonTests
{
    [Test]
    public async Task ARunnerDisconnectDuringAServerUpdateNamesTheUpdate()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var finished = DateTimeOffset.UtcNow.ToString("o");
            await File.WriteAllTextAsync(Path.Combine(directory, "status.json"),
                $"{{\"state\":\"succeeded\",\"toVersion\":\"0.2.190\",\"finishedAtUtc\":\"{finished}\"}}");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Installation:UpdateStatusDirectory"] = directory, ["Installation:Version"] = "0.2.190" }).Build();
            var service = Service(AppAuthorizationTestData.CreateDbContext(), configuration);

            var message = await service.DescribeAsync("Runner disconnected", Guid.NewGuid(), default);

            Assert.That(message, Is.EqualTo("Runner disconnected: planned restart for the update to 0.2.190."));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public async Task ARunnerDisconnectDuringARunnerUpgradeNamesTheUpgrade()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var runnerId = Guid.NewGuid();
        db.ServerMaintenanceRuns.Add(new ServerMaintenanceRun { Id = Guid.NewGuid(), Kind = ServerMaintenanceKinds.RunnerUpgrade,
            RunnerId = runnerId, StartedAtUtc = DateTime.UtcNow, State = ServerMaintenanceStates.Running });
        await db.SaveChangesAsync();

        var message = await Service(db, null).DescribeAsync("Runner connected", runnerId, default);

        Assert.That(message, Is.EqualTo("Runner connected: planned restart for the Runner upgrade."));
    }

    [Test]
    public async Task AnUnplannedDisconnectKeepsItsPlainMessage()
    {
        var message = await Service(AppAuthorizationTestData.CreateDbContext(), null)
            .DescribeAsync("Runner disconnected", Guid.NewGuid(), default, "after heartbeat timeout");

        Assert.That(message, Is.EqualTo("Runner disconnected after heartbeat timeout."));
    }

    private static RunnerConnectionSessionService Service(Automaton.Api.Data.ApplicationDbContext db, IConfiguration? configuration)
        => new(db, null!, AppAuthorizationTestData.CreateUserContextService(Guid.NewGuid()), configuration);
}
