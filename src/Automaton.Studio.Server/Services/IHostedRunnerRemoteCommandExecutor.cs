using Automaton.Studio.Server.Entities;

namespace Automaton.Studio.Server.Services;

public interface IHostedRunnerRemoteCommandExecutor
{
    Task<HostedRunnerRemoteExecutionResult> ExecuteScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken);

    Task<HostedRunnerRemoteExecutionResult> ExecuteProvisionScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken);
}
