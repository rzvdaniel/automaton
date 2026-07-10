using Automaton.Worker.Config;
using Automaton.Api.Entities;

namespace Automaton.Worker.Services;

public interface IHostedRunnerRemoteCommandExecutor
{
    bool CanHandle(RunnerHost host, HostedRunnerOptions options);

    Task<HostedRunnerRemoteExecutionResult> ExecuteProvisionScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken);
}
