using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

internal sealed class LocalApplicationBuildExecutor : IRunnerApplicationBuildExecutor
{
    public Task<ApplicationBuildExecutionResult> ExecuteAsync(
        ApplicationBuildSpec build,
        ApplicationRuntimeSpec runtime,
        string workspaceRoot,
        RunnerApplicationBuildContext? context = null,
        CancellationToken cancellationToken = default)
        => ApplicationBuildExecutor.ExecuteAsync(build, workspaceRoot, cancellationToken);
}
