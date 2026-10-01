using Automaton.Api.Config;
using Automaton.Api.Services;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class ArtifactFailureResultServices
{
    public static HostedRunnerDeploymentResultService Create(DeploymentFixtureState state, string root,
        RunnerExecutionOperationService operations)
    {
        var storage = DeploymentFixtureServiceFactory.CreateStorage(state.Db,
            new TestWebHostEnvironment { ContentRootPath = root });
        var gateway = new ManagedApplicationArtifactGateway(state.Db, operations);
        var finalization = new DeploymentPackageFinalizationService(state.Db, storage,
            new ConnectedFlowArtifactService(storage, null!, gateway), null!);
        var targetState = new AppDeploymentTargetStateService(state.Db);
        var commands = new HostedRunnerDeploymentCommandService(state.Db, null!, null!, targetState, Options.Create(new HostedRunnerOptions()));
        var store = new DeploymentPackageStore(null!, Options.Create(new DeploymentPackageStorageOptions()));
        return new(state.Db, commands, store, finalization, targetState);
    }
}
