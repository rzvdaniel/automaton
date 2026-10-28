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
            runnerDeletionService: new RunnerDeletionService(
                db,
                new RunnerDeletionPolicy(db, null!),
                audit,
                userContext, RunnerExecutionTestServices.Revocation(db)),
            connectionSessionService: sessions,
            runnerQueryService: queries,
            flowExecutionService: new RunnerFlowExecutionService(null!),
            connectionLifecycleService: new RunnerConnectionLifecycleService(
                db,
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
