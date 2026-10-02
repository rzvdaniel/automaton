using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class DeploymentDispatchCompletionServices
{
    public static HostedRunnerDeploymentResultService Create(ApplicationDbContext db, string root)
    {
        var environment = new TestWebHostEnvironment { ContentRootPath = root };
        var storage = DeploymentFixtureServiceFactory.CreateStorage(db, environment);
        var routes = new BackendRouteConfigService(db,
            Options.Create(new PublishedAppsConfig { TraefikDynamicConfigRoot = Path.Combine(root, "routes") }),
            environment, NullLogger<BackendRouteConfigService>.Instance);
        var finalization = new DeploymentPackageFinalizationService(db, storage,
            new ConnectedFlowArtifactService(storage, null!, new PackageManagedArtifactsCommands()), routes);
        var targets = new AppDeploymentTargetStateService(db);
        var commands = new HostedRunnerDeploymentCommandService(db, null!, null!, targets, Options.Create(new HostedRunnerOptions()));
        var packages = new DeploymentPackageStore(environment, Options.Create(new DeploymentPackageStorageOptions()));
        return new(db, commands, packages, finalization, targets);
    }
}
