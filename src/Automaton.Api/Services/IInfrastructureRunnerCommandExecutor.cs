using Automaton.Api.Entities;

namespace Automaton.Api.Services;

public interface IInfrastructureRunnerCommandExecutor
{
    Task<HostedRunnerRemoteExecutionResult> ExecuteScriptAsync(
        RunnerHost host,
        string script,
        CancellationToken cancellationToken);
}
