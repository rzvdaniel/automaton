using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RunnerNavigationMaterializationTests
{
    [Test]
    public async Task IncludeRunnerUsers_MaterializesUsersIntoMutableNavigation()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new ApplicationDbContext(options);
        var runnerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Runners.Add(new Runner
        {
            Id = runnerId,
            Name = "runner",
            RunnerUsers = [new RunnerUser { RunnerId = runnerId, UserId = userId }]
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var runner = await db.Runners
            .Include(item => item.RunnerUsers)
            .SingleAsync(item => item.Id == runnerId);

        Assert.That(runner.RunnerUsers, Has.Count.EqualTo(1));
        Assert.That(runner.RunnerUsers.Single().UserId, Is.EqualTo(userId));
    }
}
