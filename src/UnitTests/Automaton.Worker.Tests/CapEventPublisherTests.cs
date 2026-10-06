using Automaton.Messaging;
using Automaton.Messaging.Events;
using Automaton.Messaging.Tests;
using Automaton.Worker.Messaging;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture]
public sealed class CapEventPublisherTests
{
    [Test]
    public async Task ScheduledActionDispatch_UsesApiSubscriberTopicAndPreservesPayloadAndCancellation()
    {
        var cap = new RecordingCapPublisher();
        var publisher = new CapEventPublisher(cap);
        var message = new ScheduledActionExecutionRequestedEvent
        {
            OccurrenceId = Guid.NewGuid(), ScheduleId = Guid.NewGuid(),
            ScheduledForUtc = DateTime.UtcNow, IdempotencyKey = Guid.NewGuid().ToString("N")
        };
        using var cancellation = new CancellationTokenSource();
        await publisher.PublishAsync(message, cancellation.Token);

        Assert.That(cap.Messages, Has.Count.EqualTo(1));
        var sent = cap.Messages.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Topic, Is.EqualTo(EventTopics.ScheduledActionExecutionRequested));
            Assert.That(sent.Message, Is.SameAs(message));
            Assert.That(sent.CancellationToken, Is.EqualTo(cancellation.Token));
        });
    }

    [Test]
    public void CancelledScheduledDispatch_DoesNotPublish()
    {
        var cap = new RecordingCapPublisher();
        var publisher = new CapEventPublisher(cap);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => publisher.PublishAsync(
            new ScheduledActionExecutionRequestedEvent(), cancellation.Token));
        Assert.That(cap.Messages, Is.Empty);
    }
}
