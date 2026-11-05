using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Enums;

namespace Automaton.Api.Tests;

public sealed class RestoredRunnerStatusTests
{
    [Test]
    public async Task RunnerRestoredWithAConnectionButNoRecentHeartbeatIsListedOffline()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var restored = AddRunner(db, owner, "restored", "old-connection", DateTime.UtcNow.AddDays(-2));
        var live = AddRunner(db, owner, "live", "current-connection", DateTime.UtcNow);
        await db.SaveChangesAsync();
        var user = AppAuthorizationTestData.CreateUserContextService(owner);
        var service = new RunnerQueryService(db, new ResourceAuthorizationService(db, user), user,
            new RunnerConnectionSessionService(db, null!, user));

        var runners = (await service.List(default)).ToDictionary(runner => runner.Id);

        Assert.That(runners[restored].Status, Is.EqualTo(RunnerStatus.Offline));
        Assert.That(runners[restored].ConnectionId, Is.Null);
        Assert.That(runners[live].Status, Is.EqualTo(RunnerStatus.Online));
        Assert.That(runners[live].ConnectionId, Is.EqualTo("current-connection"));
    }

    private static Guid AddRunner(Automaton.Api.Data.ApplicationDbContext db, Guid owner, string name, string connection, DateTime heartbeat)
    {
        var id = Guid.NewGuid();
        db.Runners.Add(new Runner { Id = id, Name = name, Purpose = RunnerPurposes.Managed,
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement, Status = "Online",
            ConnectionId = connection, LastHeartbeatAtUtc = heartbeat });
        db.RunnerUsers.Add(new RunnerUser { RunnerId = id, UserId = owner });
        return id;
    }
}
