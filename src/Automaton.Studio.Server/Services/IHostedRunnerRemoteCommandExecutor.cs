using Automaton.Api.Entities;

namespace Automaton.Api.Services;

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
