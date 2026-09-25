using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class HostedRunnerRegistrationReconcilerTests
{
    [Test]
    public async Task TimeoutRevokesEnrollmentReleasesCapacityAndPublishesExactlyOnce()
    {
        await using var scenario = await HostedRunnerRegistrationTimeoutScenario.CreateAsync();
        await scenario.ReconcileAndAssertAsync();
    }
}
