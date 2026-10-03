using Automaton.Api.Data;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

internal static class AppAuthorizationActionExecutionTestFactory
{
    public static AppActionExecutionService Create(ApplicationDbContext dbContext, Guid userId)
    {
        var userContext = AppAuthorizationTestData.CreateUserContextService(userId);
        var authorization = new ResourceAuthorizationService(dbContext, userContext);
        var runnerAuthorization = new AppRunnerAuthorizationPolicy(dbContext, authorization);
        var targetResolver = new AppDeploymentTargetResolver(dbContext, authorization, runnerAuthorization);
        var assignment = new AppRunnerAssignmentPolicy(
            dbContext,
            new HostedRunnerAppCapacityService(dbContext),
            runnerAuthorization,
            targetResolver);
        var apps = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, userId);
        return new AppActionExecutionService(dbContext, apps, authorization, assignment, targetResolver, null!);
    }
}
