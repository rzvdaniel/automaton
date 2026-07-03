using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Identity.Services;

namespace Automaton.Worker.Handlers;

public sealed class UserRegisteredEventHandler(
    ILogger<UserRegisteredEventHandler> logger,
    UserRegistrationNotificationService userRegistrationNotificationService) : IEventHandler<UserRegisteredEvent>
{
    public async Task HandleAsync(UserRegisteredEvent message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await userRegistrationNotificationService.SendRegistrationNotificationsAsync(
                message.UserId,
                cancellationToken);

            logger.LogDebug(
                "Completed registration notifications for user {UserId} with correlation {CorrelationId}",
                message.UserId,
                message.CorrelationId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Registration notifications failed for user {UserId} ({Email}) with correlation {CorrelationId}",
                message.UserId,
                message.Email,
                message.CorrelationId);

            throw;
        }
    }
}
