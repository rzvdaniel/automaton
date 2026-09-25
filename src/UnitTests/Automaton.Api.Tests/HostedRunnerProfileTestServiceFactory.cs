using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Tests;

internal static class HostedRunnerProfileTestServiceFactory
{
    public static RunnerService Create(
        ApplicationDbContext db,
        UserContextService userContext,
        IAuditEventService? auditEventService = null)
    {
        var audit = auditEventService ?? new HostedRunnerAuditNoOpAuditEventService();
        var sessions = CreateSessions(db, userContext);
        var queries = new RunnerQueryService(db, null!, userContext, sessions);

        return new RunnerService(
            db,
            userContext,
            authorizationService: null!,
            runnerRegistrationService: new RunnerRegistrationService(db, userContext, new RunnerAuditService(audit)),
            runnerDeletionService: new RunnerDeletionService(
                db,
                new RunnerDeletionPolicy(db, null!),
                audit,
                userContext),
            connectionSessionService: sessions,
            runnerQueryService: queries,
            restartCompletionService: new HostedRunnerRestartCompletionService(db, audit),
            flowExecutionService: new RunnerFlowExecutionService(queries, null!),
            instanceMigrationService: new RunnerInstanceMigrationService(db, new ApiInstanceIdentityService(), null!),
            connectionLifecycleService: new RunnerConnectionLifecycleService(
                db,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!));
    }

    public static RunnerService CreateWithAudit(ApplicationDbContext db, Guid userId)
    {
        var httpContextAccessor = HostedRunnerAuditTestHttpContext.Create(userId, "runner-owner@automaton.test");
        var userContext = new UserContextService(httpContextAccessor);
        return Create(db, userContext, new AuditEventService(db, userContext, httpContextAccessor));
    }

    private static RunnerConnectionSessionService CreateSessions(
        ApplicationDbContext db,
        UserContextService userContext)
        => new(db, null!, userContext);
}
