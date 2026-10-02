using Automaton.Api.Entities;

namespace Automaton.Api.Tests;

public sealed class DeploymentDispatchConcurrencyTests
{
    [Test]
    public async Task WaitingDeploymentDoesNotBlockAnotherRunnerOrTheNextPersistedDeployment()
    {
        await using var scenario = await DeploymentDispatchConcurrencyScenario.CreateAsync();
        var first = await scenario.AddDeploymentAsync();
        await scenario.StartAsync();
        var firstPublication = await scenario.WaitForPublicationAsync(first);

        // Admit B after A is already waiting, rather than in the same initial scan.
        var second = await scenario.AddDeploymentAsync();
        await scenario.NotifyAsync(second);
        await scenario.NotifyAsync(first);
        var secondPublication = await scenario.WaitForPublicationAsync(second);
        Assert.Multiple(() =>
        {
            Assert.That(secondPublication.RunnerId, Is.Not.EqualTo(firstPublication.RunnerId));
            Assert.That(secondPublication.Context, Is.Not.SameAs(firstPublication.Context));
        });

        // Both configured slots are occupied. C stays durable until B releases a slot.
        var third = await scenario.AddDeploymentAsync();
        await scenario.NotifyAsync(third);
        Assert.That(await Task.WhenAny(scenario.Tracker.Publication(third), Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None)),
            Is.Not.SameAs(scenario.Tracker.Publication(third)));
        Assert.That(await scenario.StatusAsync(third), Is.EqualTo(DeploymentOperationStatuses.Pending));
        await scenario.CompleteAsync(second);
        _ = await scenario.WaitForPublicationAsync(third);
        await scenario.CompleteAsync(third);
        Assert.Multiple(() =>
        {
            Assert.That(scenario.Tracker.Count(first), Is.EqualTo(1));
            Assert.That(scenario.Tracker.Count(second), Is.EqualTo(1));
            Assert.That(scenario.Tracker.Count(third), Is.EqualTo(1));
        });
        Assert.That(await scenario.StatusAsync(first), Is.EqualTo(DeploymentOperationStatuses.Running));
        Assert.That(await scenario.StatusAsync(second), Is.EqualTo(DeploymentOperationStatuses.Succeeded));
        Assert.That(await scenario.StatusAsync(third), Is.EqualTo(DeploymentOperationStatuses.Succeeded));
        await scenario.StopAsync();
        Assert.That(await scenario.StatusAsync(first), Is.EqualTo(DeploymentOperationStatuses.Running));
        Assert.Throws<ObjectDisposedException>(() => firstPublication.Context.DeploymentOperations.Any());
        Assert.Throws<ObjectDisposedException>(() => secondPublication.Context.DeploymentOperations.Any());
    }
}
