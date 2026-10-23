using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Tests;

internal static class ManagedRunnerBootstrapTestServiceFactory
{
    public static ManagedRunnerBootstrapService CreateService(
        ApplicationDbContext db)
    {
        var runnerAssignmentPolicy = CreateRunnerAssignmentPolicy(db);
        return new ManagedRunnerBootstrapService(db, runnerAssignmentPolicy);
    }

    public static AppRunnerAssignmentPolicy CreateRunnerAssignmentPolicy(ApplicationDbContext db)
    {
        var userContextService = new UserContextService(new HttpContextAccessor());
        var authorizationService = new ResourceAuthorizationService(db, userContextService);
        var runnerAuthorizationPolicy = new AppRunnerAuthorizationPolicy(db, authorizationService);
        var deploymentTargetResolver = new AppDeploymentTargetResolver(db, authorizationService, runnerAuthorizationPolicy);
        return new AppRunnerAssignmentPolicy(
            db,
            runnerAuthorizationPolicy,
            deploymentTargetResolver);
    }

}
