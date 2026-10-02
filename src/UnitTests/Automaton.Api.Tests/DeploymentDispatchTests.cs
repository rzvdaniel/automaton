using Automaton.Api.Entities;
using Automaton.Api.Enums;

namespace Automaton.Api.Tests;

public sealed class DeploymentDispatchTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task SerialCapDeliveryCanCompleteArtifactsWhileDeploymentWaitsForResult(bool notify)
    {
        await using var scenario = await DeploymentDispatchScenario.CreateAsync();
        if (notify) await scenario.DeliverDeploymentAsync();
        Assert.That(scenario.Operation.Status, Is.EqualTo(DeploymentOperationStatuses.Pending));
        await scenario.StartDispatcherAsync();
        var callback = scenario.ReportPromotionAsync();
        // Redelivery must also release the CAP delivery slot while the parent is waiting.
        await scenario.DeliverDeploymentAsync();
        await scenario.DeliverDeploymentAsync();
        await scenario.DeliverManagementAsync();
        Assert.That(await callback.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo("accepted"));
        await scenario.StopDispatcherAsync();
        await scenario.ReloadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(scenario.Operation.Status, Is.EqualTo(DeploymentOperationStatuses.Failed));
            Assert.That(scenario.Operation.CompletedAtUtc, Is.Not.Null);
            Assert.That(scenario.Target.Status, Is.EqualTo(DeploymentTargetStatus.Failed));
            Assert.That(scenario.Target.ActiveDeploymentOperationId, Is.Null);
            Assert.That(scenario.HostDispatchCount, Is.EqualTo(1));
            Assert.That(scenario.PackageExists, Is.False);
        });
        Assert.That(await scenario.ReportPromotionAsync(), Is.EqualTo("already-applied"));
    }

    [Test]
    public async Task StoppingDispatcherPreservesUnfinishedOperationLeaseAndPackage()
    {
        await using var scenario = await DeploymentDispatchScenario.CreateAsync();
        await scenario.StartDispatcherAsync();
        await scenario.DeliverDeploymentAsync();
        await scenario.StopDispatcherAsync();
        await scenario.ReloadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(scenario.Operation.Status, Is.EqualTo(DeploymentOperationStatuses.Running));
            Assert.That(scenario.Operation.CompletedAtUtc, Is.Null);
            Assert.That(scenario.Operation.ExecutionAttemptId, Is.Not.Null);
            Assert.That(scenario.Operation.LeaseExpiresAtUtc, Is.GreaterThan(DateTime.UtcNow));
            Assert.That(scenario.HostDispatchCount, Is.EqualTo(1));
            Assert.That(scenario.PackageExists, Is.True);
        });
    }
}
