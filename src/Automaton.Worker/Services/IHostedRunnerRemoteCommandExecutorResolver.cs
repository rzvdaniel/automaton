using Automaton.Api.Entities;
using Automaton.Worker.Config;

namespace Automaton.Worker.Services;

public interface IHostedRunnerRemoteCommandExecutorResolver
{
    IHostedRunnerRemoteCommandExecutor Resolve(RunnerHost host, HostedRunnerOptions options);
}
