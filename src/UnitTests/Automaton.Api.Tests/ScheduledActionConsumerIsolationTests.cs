using Automaton.Api.Messaging;
using Automaton.Messaging;
using DotNetCore.CAP;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ScheduledActionConsumerIsolationTests
{
    [Test]
    public void ScheduledDispatchCannotOccupyRunnerCommandConsumerGroup()
    {
        var scheduled = SubscriptionFor(typeof(ScheduledActionExecutionRequestedCapSubscriber));
        var runnerCommands = SubscriptionFor(typeof(RunnerExecutionRequestedCapSubscriber));
        Assert.Multiple(() =>
        {
            Assert.That(scheduled.Name, Is.EqualTo(EventTopics.ScheduledActionExecutionRequested));
            Assert.That(runnerCommands.Name, Is.EqualTo(EventTopics.RunnerExecutionRequested));
            Assert.That(scheduled.Group, Is.Not.Null.And.Not.Empty);
            Assert.That(scheduled.Group, Is.Not.EqualTo(runnerCommands.Group),
                "Scheduled dispatch waits for Runner commands; sharing the single-thread CAP group deadlocks delivery.");
        });
    }

    private static CapSubscribeAttribute SubscriptionFor(Type subscriber)
        => subscriber.GetMethod("HandleAsync")!
            .GetCustomAttributes(typeof(CapSubscribeAttribute), inherit: false)
            .Cast<CapSubscribeAttribute>().Single();
}
