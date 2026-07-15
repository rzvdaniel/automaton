using Automaton.Api.Entities;
using Automaton.Worker.Config;

namespace Automaton.Worker.Services;

public sealed class HostedRunnerRemoteCommandExecutorResolver(
    IEnumerable<IHostedRunnerRemoteCommandExecutor> executors) : IHostedRunnerRemoteCommandExecutorResolver
{
    public IHostedRunnerRemoteCommandExecutor Resolve(RunnerHost host, HostedRunnerOptions options)
    {
        var matches = executors
            .Where(candidate => candidate.CanHandle(host, options))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No hosted runner provisioner can handle transport '{host.ProvisioningTransport}' with deployment mode '{options.DeploymentMode}'."),
            _ => throw new InvalidOperationException(
                $"Multiple hosted runner provisioners matched transport '{host.ProvisioningTransport}' with deployment mode '{options.DeploymentMode}'.")
        };
    }
}
