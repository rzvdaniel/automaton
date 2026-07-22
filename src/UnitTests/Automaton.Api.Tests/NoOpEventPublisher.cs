using Automaton.Messaging.Abstractions;

namespace Automaton.Api.Tests;

internal sealed class NoOpEventPublisher : IEventPublisher
{
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        => Task.CompletedTask;
}
