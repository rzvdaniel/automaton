using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;

namespace Automaton.Worker.Handlers;

public sealed class UserRegisteredEventHandler(
    ILogger<UserRegisteredEventHandler> logger) : IEventHandler<UserRegisteredEvent>
{
    public Task HandleAsync(UserRegisteredEvent message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation(
            "Consumed {EventName} for user {UserId} ({Email}) with correlation {CorrelationId}",
            nameof(UserRegisteredEvent),
            message.UserId,
            message.Email,
            message.CorrelationId);

        return Task.CompletedTask;
    }
}
