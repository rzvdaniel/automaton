using Automaton.Api.Services;
using Automaton.Messaging;
using Automaton.Messaging.Events;
using DotNetCore.CAP;

namespace Automaton.Api.Messaging;

public sealed class RunnerExecutionResultReadyCapSubscriber(RunnerExecutionCommandPublisher publisher) : ICapSubscribe
{
    [CapSubscribe(EventTopics.RunnerExecutionResultReady, Group = "automaton.api")]
    public Task HandleAsync(RunnerExecutionResultReadyEvent message, CancellationToken token)
        => publisher.PublishResultReadyAsync(message.RunnerId, message.OperationId, token);
}
