using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Models;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class AppAuthorizationActionExecutionTestFactory
{
    public static AppActionExecutionService Create(
        ApplicationDbContext dbContext,
        Guid userId,
        IAppActionExecutionGateway? gateway = null)
    {
        var userContext = AppAuthorizationTestData.CreateUserContextService(userId);
        var authorization = new ResourceAuthorizationService(dbContext, userContext);
        var runnerAuthorization = new AppRunnerAuthorizationPolicy(dbContext, authorization);
        var targetResolver = new AppDeploymentTargetResolver(dbContext, authorization, runnerAuthorization);
        var apps = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, userId);
        return new AppActionExecutionService(
            dbContext, apps, authorization, targetResolver, gateway!, Options.Create(new MachineContainerResourceOptions()));
    }
}
