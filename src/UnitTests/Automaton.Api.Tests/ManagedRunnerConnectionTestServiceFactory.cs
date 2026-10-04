using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

internal static class ManagedRunnerConnectionTestServiceFactory
{
    public static ManagedRunnerAppAssignmentService CreateWithFailingRoutes(
        ApplicationDbContext db,
        ResourceAuthorizationService authorization,
        BackendRouteConfigService failingRoutes) =>
        new(db, null!, authorization, failingRoutes,
            NullLogger<ManagedRunnerAppAssignmentService>.Instance);

    public static RunnerService Create(
        ApplicationDbContext db,
        UserContextService userContext,
        ResourceAuthorizationService authorization,
        ManagedRunnerAppAssignmentService appAssignment)
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
                registrationService, appAssignment, restartService));
    }
}
