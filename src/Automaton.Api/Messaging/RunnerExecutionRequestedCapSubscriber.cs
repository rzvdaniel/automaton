using Automaton.Api.Services;
using Automaton.Core.Models;
using Automaton.Messaging;
using Automaton.Messaging.Events;
using DotNetCore.CAP;

namespace Automaton.Api.Messaging;

public sealed class RunnerExecutionRequestedCapSubscriber(RunnerExecutionCommandPublisher publisher) : ICapSubscribe
{
    [CapSubscribe(EventTopics.RunnerExecutionRequested, Group = "automaton.api")]
    public Task HandleAsync(RunnerExecutionRequestedEvent message, CancellationToken token)
        => publisher.PublishAsync(new RunnerExecutionCommand(message.OperationId, message.RunnerId), token);
}
