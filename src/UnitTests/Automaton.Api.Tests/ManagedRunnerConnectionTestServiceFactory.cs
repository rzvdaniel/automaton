using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Tests;

internal static class ManagedRunnerConnectionTestServiceFactory
{
    public static RunnerService Create(ApplicationDbContext db)
    {
        var userContext = new UserContextService(new HttpContextAccessor());
        return Create(db, userContext, new ResourceAuthorizationService(db, userContext));
    }

    public static Task MarkDisconnectedAsync(ApplicationDbContext db, Guid runnerId, string connectionId)
    {
        var service = new RunnerConnectionSessionService(
            db,
            new ApplicationEventService(db),
            new UserContextService(new HttpContextAccessor()));
        return service.MarkDisconnectedAsync(runnerId, connectionId, CancellationToken.None);
    }

    public static RunnerService Create(
        ApplicationDbContext db,
        UserContextService userContext,
        ResourceAuthorizationService authorization)
    {
        var auditEventService = new AuditEventService(db, userContext, new HttpContextAccessor());
        var applicationEventService = new ApplicationEventService(db);
        var sessionService = new RunnerConnectionSessionService(db, applicationEventService, userContext);
        var registrationService = new RunnerRegistrationService(db, userContext, new RunnerAuditService(auditEventService));
        var restartService = new HostedRunnerRestartCompletionService(db, auditEventService);
        var queryService = new RunnerQueryService(db, authorization, userContext, sessionService);

        return new RunnerService(
            db,
            userContext,
            authorization,
            registrationService,
            new RunnerDeletionService(db, new RunnerDeletionPolicy(db, authorization), auditEventService, userContext, RunnerExecutionTestServices.Revocation(db)),
            sessionService,
            queryService,
            restartService,
            new RunnerFlowExecutionService(null!),
            new RunnerConnectionLifecycleService(
                db, authorization, new ApiInstanceIdentityService(), sessionService,
                registrationService, restartService));
    }
}
