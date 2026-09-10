using Automaton.Core.Models;
using Automaton.Core.Services;

namespace Automaton.Runner.Console.Services;

public interface IRunnerApplicationBuildExecutor
{
    Task<ApplicationBuildExecutionResult> ExecuteAsync(
        ApplicationBuildSpec build,
        ApplicationRuntimeSpec runtime,
        string workspaceRoot,
        RunnerApplicationBuildContext? context = null,
        CancellationToken cancellationToken = default);
}

public sealed class RunnerApplicationBuildExecutor : IRunnerApplicationBuildExecutor
{
    private readonly IApplicationContainerRuntime containers;

    public RunnerApplicationBuildExecutor(IApplicationContainerRuntime containers)
    {
        this.containers = containers;
    }

    public async Task<ApplicationBuildExecutionResult> ExecuteAsync(
        ApplicationBuildSpec build,
        ApplicationRuntimeSpec runtime,
        string workspaceRoot,
        RunnerApplicationBuildContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (build.Command is null)
            return new ApplicationBuildExecutionResult { Succeeded = true };

        var image = build.Image ?? runtime.Image;
        if (string.IsNullOrWhiteSpace(image))
            throw new InvalidOperationException("A build image or runtime image is required for build execution.");

        try
        {
            await containers.RunBuildAsync(
                new DockerBuildRequest(image, build.Command, workspaceRoot, build.TimeoutSeconds, context),
                build.Command.Environment,
                cancellationToken);
            return new ApplicationBuildExecutionResult { Succeeded = true };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ApplicationBuildExecutionResult { Canceled = true, Error = "Application build was canceled." };
        }
        catch (OperationCanceledException)
        {
            return new ApplicationBuildExecutionResult { TimedOut = true, Error = $"Application build timed out after {build.TimeoutSeconds} seconds." };
        }
        catch (Exception ex)
        {
            return new ApplicationBuildExecutionResult { Error = ex.Message, StandardError = [ex.Message] };
        }
    }
}
