using Automaton.Messaging.Events;
using Automaton.Messaging.Tests;
using Automaton.Worker.Messaging;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class InfrastructureCommandPublicationTests
{
    [Test]
    public async Task PublishesConvertedRequestToCommandTopicOnly()
    {
        var cap = new RecordingCapPublisher();
        var publisher = new CapEventPublisher(cap);
        var request = new InfrastructureActionExecutionRequestedEvent
        {
            OperationId = Guid.NewGuid(), RunnerHostId = Guid.NewGuid(),
            ActionKey = "provision-hosted-runner", InputJson = "{}", SecretGrantIds = [Guid.NewGuid()]
        };
        var command = request.ToInfrastructureCommand(Guid.NewGuid(), DateTime.UtcNow);
        using var cancellation = new CancellationTokenSource();

        await publisher.PublishAsync(command, cancellation.Token);

        Assert.That(cap.Messages, Has.Count.EqualTo(1));
        Assert.That(cap.Messages.Single(), Is.EqualTo(("infrastructure-command.requested", command, cancellation.Token)));
    }

    [Test]
    public async Task ExistingRequestPublishesOnlyToExistingTopic()
    {
        var cap = new RecordingCapPublisher();
        var request = new InfrastructureActionExecutionRequestedEvent();

        await new CapEventPublisher(cap).PublishAsync(request);

        Assert.That(cap.Messages, Has.Count.EqualTo(1));
        Assert.That(cap.Messages.Single(), Is.EqualTo(("infrastructure-action.execution-requested", request, CancellationToken.None)));
    }
}
