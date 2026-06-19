using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;

namespace Automaton.Api.Services;

public sealed class AccountRegistrationService(
    ApplicationDbContext dataContext,
    UsersService userManagerService,
    IEventPublisher eventPublisher,
    ITransactionalEventExecutor transactionalEventExecutor)
{
    public async Task RegisterAsync(
        ApplicationUser user,
        string password,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await transactionalEventExecutor.ExecuteAsync(async innerCancellationToken =>
        {
            await userManagerService.CreateUser(user, password);
            await dataContext.SaveChangesAsync(innerCancellationToken);
            await eventPublisher.PublishAsync(
                new UserRegisteredEvent(
                    user.Id,
                    user.Email ?? string.Empty,
                    user.Name,
                    user.PublicUsername,
                    user.RegisteredAtUtc,
                    correlationId),
                innerCancellationToken);
        }, cancellationToken);
    }
}
