using Automaton.Api.Data;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;

namespace Automaton.Api.Services;

public sealed class AccountRegistrationService(
    ApplicationDbContext dataContext,
    UsersService userManagerService,
    IAuditEventService auditEventService,
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
            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Created,
                EntityType = AuditEntityTypes.User,
                EntityId = user.Id,
                EventType = AuditEventTypes.UserRegistered,
                Message = "User registered.",
                Outcome = AuditOutcomes.Success,
                TargetUserId = user.Id,
                Metadata = new
                {
                    user.PublicUsername,
                    user.RegistrationProvider,
                    user.RegisteredAtUtc,
                    CorrelationId = correlationId
                }
            }, innerCancellationToken);
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
