using Automaton.Runner.Console.Models;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace Automaton.Runner.Console.Services;

/// <summary>Generic deployment transfer remains until RM Slice 4.</summary>
internal sealed class HubAppActionHandlerRegistrar(RunnerDeploymentService runnerDeploymentService)
{
    public void Register(HubConnection connection,
        Func<Func<Task<DeployAppPackageResult>>, Task<DeployAppPackageResult>> executeWithActivity)
        => connection.On<DeployAppPackageRequest, DeployAppPackageResult>("DeployAppPackage", request =>
            executeWithActivity(() => runnerDeploymentService.DeployPackage(request)));
}
