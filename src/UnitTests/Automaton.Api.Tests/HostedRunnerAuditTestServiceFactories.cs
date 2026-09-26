using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class HostedRunnerProvisioningTestServiceFactory
{
    public static HostedRunnerProvisioningService Create(
        ApplicationDbContext db,
        Guid actorUserId,
        HostedRunnerOptions options)
    {
        var httpContextAccessor = HostedRunnerAuditTestHttpContext.Create(actorUserId, "admin@automaton.test");
        var userContextService = new UserContextService(httpContextAccessor);
        IAuditEventService auditEventService = new AuditEventService(db, userContextService, httpContextAccessor);
        var hostedRunnerOptions = new HostedRunnerTestOptionsSnapshot<HostedRunnerOptions>(options);
        var hostPlacementService = new HostedRunnerHostPlacementService(db, hostedRunnerOptions);
        var preparationService = CreatePreparationService(
            db,
            auditEventService,
            hostedRunnerOptions,
            hostPlacementService,
            options);

        return new HostedRunnerProvisioningService(
            db,
            new NoOpEventPublisher(),
            hostedRunnerOptions,
            CreatePolicyService(db),
            new AccountStorageProjectAllocationService(db),
            new HostedRunnerStorageLocalityResolver(db),
            NullLogger<HostedRunnerProvisioningService>.Instance,
            preparationService);
    }

    private static HostedRunnerProvisioningPreparationService CreatePreparationService(
        ApplicationDbContext db,
        IAuditEventService auditEventService,
        HostedRunnerTestOptionsSnapshot<HostedRunnerOptions> options,
        HostedRunnerHostPlacementService hostPlacementService,
        HostedRunnerOptions hostedRunnerOptions) =>
        new(
            db,
            TestRunnerEnrollmentFactory.Create(db, auditEventService, hostedRunnerOptions),
            auditEventService,
            options,
            hostPlacementService,
            NullLogger<HostedRunnerProvisioningPreparationService>.Instance);

    private static HostedRunnerProvisioningPolicyService CreatePolicyService(ApplicationDbContext db) =>
        new(
            db,
            new SubscriptionResourcePolicyResolver(new HostedRunnerSpecificationResolver(db)),
            NullLogger<HostedRunnerProvisioningPolicyService>.Instance);
}

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
        var options = new HostedRunnerTestOptionsSnapshot<HostedRunnerOptions>(new HostedRunnerOptions { Enabled = true });
        var provisioningService = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            actorUserId,
            options.Value);

        return new HostedRunnerManagementService(
            db,
            provisioningService,
            eventPublisher ?? new NoOpEventPublisher(),
            auditEventService,
            userContextService,
            NullLogger<HostedRunnerManagementService>.Instance);
    }
}

internal sealed class HostedRunnerTestOptionsSnapshot<T>(T value) : IOptionsSnapshot<T> where T : class
{
    public T Value => value;
    public T Get(string? name) => value;
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
