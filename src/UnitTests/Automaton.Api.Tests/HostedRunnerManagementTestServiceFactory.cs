using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

internal static class HostedRunnerManagementTestServiceFactory
{
    public static HostedRunnerManagementService Create(
        ApplicationDbContext db,
        Guid actorUserId,
        IEventPublisher? eventPublisher = null)
    {
        var httpContextAccessor = HostedRunnerAuditTestHttpContext.Create(actorUserId, "admin@automaton.test");
        var userContextService = new UserContextService(httpContextAccessor);
        IAuditEventService auditEventService = new AuditEventService(db, userContextService, httpContextAccessor);
        return new HostedRunnerManagementService(
            db,
            eventPublisher ?? new HostedRunnerRecordingEventPublisher(),
            auditEventService,
            userContextService,
            NullLogger<HostedRunnerManagementService>.Instance);
    }
}

internal sealed class HostedRunnerRecordingEventPublisher : IEventPublisher
{
    public List<object> Messages { get; } = [];

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}
