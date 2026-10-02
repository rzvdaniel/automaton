using Automaton.Api.Services;
using Automaton.Messaging;
using Automaton.Messaging.Events;
using DotNetCore.CAP;

namespace Automaton.Api.Messaging;

public sealed class DeploymentPackageRequestedCapSubscriber(
    DeploymentOperationDispatcher dispatcher) : ICapSubscribe
{
    [CapSubscribe(EventTopics.DeploymentPackageRequested, Group = "automaton.api")]
    public Task HandleAsync(DeploymentPackageRequestedEvent message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Never hold this CAP group while a deployment waits for a Runner command
        // delivered through the same group. Recovery scans also cover lost wakeups.
        dispatcher.Notify();
        return Task.CompletedTask;
    }
}
