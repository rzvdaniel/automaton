using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppActionRunnerGatewayTests
{
    [Test]
    public async Task ACommandToARunnerThatNeverAnswers_FailsWithATimeoutInsteadOfWaitingForever()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var runnerId = Guid.NewGuid();
        db.Runners.Add(new Runner { Id = runnerId, ApiKeyId = Guid.NewGuid(), Name = "offline", RunnerUsers = [] });
        await db.SaveChangesAsync();
        var gateway = new AppActionRunnerGateway(RunnerExecutionTestServices.Operations(db),
            AppAuthorizationTestData.CreateUserContextService(Guid.NewGuid()));

        var send = gateway.ListModulesAsync(runnerId, Guid.NewGuid(), Guid.NewGuid(), "work", CancellationToken.None);
        var finished = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(8)));

        Assert.That(finished, Is.SameAs(send), "the call is still waiting for the Runner after 8 seconds");
        Assert.ThrowsAsync<TimeoutException>(async () => await send);
    }
}
