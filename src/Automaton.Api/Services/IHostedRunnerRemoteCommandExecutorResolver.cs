using Automaton.Api.Config;
using Automaton.Api.Entities;

namespace Automaton.Api.Services;

public interface IHostedRunnerRemoteCommandExecutorResolver
{
    IHostedRunnerRemoteCommandExecutor Resolve(RunnerHost host, HostedRunnerOptions options);
}
