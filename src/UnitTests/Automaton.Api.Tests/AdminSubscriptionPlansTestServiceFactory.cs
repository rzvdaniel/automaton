using Automaton.Api.Config;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Messaging;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Automaton.Api.Tests;

internal static class AdminSubscriptionPlansTestServiceFactory
{
    public static AdminSubscriptionPlansController CreateController(
        ApplicationDbContext dbContext,
        Guid actorUserId,
        ITransactionalEventExecutor? transactionalEventExecutor = null,
        IAuditEventService? auditEventService = null)
    {
        var httpContextAccessor = CreateHttpContextAccessor(actorUserId);
        var userContextService = new UserContextService(httpContextAccessor);
        auditEventService ??= new AuditEventService(dbContext, userContextService, httpContextAccessor);
        var planOperations = CreatePlanOperations(dbContext, auditEventService, transactionalEventExecutor);
        return new AdminSubscriptionPlansController(dbContext, new AdminPlanTestUserManager(), planOperations);
    }

    public static IAuditEventService CreateAuditEventService(ApplicationDbContext dbContext, Guid actorUserId)
    {
        var httpContextAccessor = CreateHttpContextAccessor(actorUserId);
        return new AuditEventService(dbContext, new UserContextService(httpContextAccessor), httpContextAccessor);
    }

    private static AdminSubscriptionPlanOperationsService CreatePlanOperations(
        ApplicationDbContext dbContext,
        IAuditEventService auditEventService,
        ITransactionalEventExecutor? transactionalEventExecutor)
    {
        var hostedRunnerOptions = new AdminPlanTestOptionsSnapshot<HostedRunnerOptions>(new HostedRunnerOptions
        {
            Enabled = false
        });
        var preparationService = CreateProvisioningPreparation(dbContext, auditEventService, hostedRunnerOptions);
        var provisioningService = CreateProvisioningService(dbContext, hostedRunnerOptions, preparationService);
        return new AdminSubscriptionPlanOperationsService(
            dbContext,
            provisioningService,
            new HostedRunnerPlanCapabilityValidator(new HostedRunnerSpecificationResolver(dbContext)),
            auditEventService,
            transactionalEventExecutor ?? new AdminPlanInlineTransactionalEventExecutor());
    }

    private static HostedRunnerProvisioningPreparationService CreateProvisioningPreparation(
        ApplicationDbContext dbContext,
        IAuditEventService auditEventService,
        IOptionsSnapshot<HostedRunnerOptions> hostedRunnerOptions)
        => new(
            dbContext,
            TestRunnerEnrollmentFactory.Create(dbContext, auditEventService),
            auditEventService,
            hostedRunnerOptions,
            new HostedRunnerHostPlacementService(dbContext, hostedRunnerOptions),
            NullLogger<HostedRunnerProvisioningPreparationService>.Instance);

    private static HostedRunnerProvisioningService CreateProvisioningService(
        ApplicationDbContext dbContext,
        IOptionsSnapshot<HostedRunnerOptions> hostedRunnerOptions,
        HostedRunnerProvisioningPreparationService preparationService)
        => new(
            dbContext,
            new NoOpEventPublisher(),
            hostedRunnerOptions,
            new HostedRunnerProvisioningPolicyService(
                dbContext,
                new SubscriptionResourcePolicyResolver(new HostedRunnerSpecificationResolver(dbContext)),
                NullLogger<HostedRunnerProvisioningPolicyService>.Instance),
            new AccountStorageProjectAllocationService(dbContext),
            NullLogger<HostedRunnerProvisioningService>.Instance,
            preparationService);

    private static IHttpContextAccessor CreateHttpContextAccessor(Guid userId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, "admin@automaton.test")
            ], "Test"))
        };
        return new HttpContextAccessor { HttpContext = httpContext };
    }
}

internal sealed class AdminPlanTestOptionsSnapshot<T>(T value) : IOptionsSnapshot<T> where T : class
{
    public T Value => value;
    public T Get(string? name) => value;
}

internal sealed class AdminPlanInlineTransactionalEventExecutor(Action? onExecute = null) : ITransactionalEventExecutor
{
    public CancellationToken LastToken { get; private set; }

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        LastToken = cancellationToken;
        onExecute?.Invoke();
        return operation(cancellationToken);
    }
}
