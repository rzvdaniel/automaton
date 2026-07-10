using Automaton.Api.Config;
using Automaton.Api.Entities;

namespace Automaton.Api.Services;

public interface IHostedRunnerRemoteCommandExecutor
{
    bool CanHandle(RunnerHost host, HostedRunnerOptions options);

    Task<HostedRunnerRemoteExecutionResult> ExecuteScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken);

    Task<HostedRunnerRemoteExecutionResult> ExecuteProvisionScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken);
}
