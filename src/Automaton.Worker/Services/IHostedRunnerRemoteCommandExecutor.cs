using Automaton.Api.Entities;

namespace Automaton.Worker.Services;

public interface IHostedRunnerRemoteCommandExecutor
{
    Task<HostedRunnerRemoteExecutionResult> ExecuteProvisionScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken);
}
