using Automaton.Api.Models;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class HostedRunnerStorageEnforcementTests
{
    [Test]
    public async Task AuthoritativeOverLimit_StartsGraceThenQueuesOneSuspension()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync(gracePeriodHours: 1);
        await fixture.SeedOverLimitRunnerAsync();
        await fixture.EnforceAsync();
        Assert.That(await fixture.EnforcementStateAsync(), Is.EqualTo(HostedRunnerEnforcementStates.GracePeriod));

        await fixture.EndGracePeriodAsync();
        await fixture.EnforceAsync();

        var command = fixture.SingleEnforcementCommand();
        var state = await fixture.EnforcementStateAsync();
        var suspensionCount = await fixture.SuspensionDeploymentCountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(HostedRunnerEnforcementStates.SuspensionPending));
            Assert.That(suspensionCount, Is.EqualTo(1));
            Assert.That(command.ActionKey, Is.EqualTo(StorageEnforcementActions.SuspendHostedRunner));
            Assert.That(command.RunnerHostId, Is.EqualTo(fixture.HostId));
            Assert.That(command.InfrastructureRunnerId, Is.EqualTo(fixture.InfrastructureRunnerId));
        });
    }
}
