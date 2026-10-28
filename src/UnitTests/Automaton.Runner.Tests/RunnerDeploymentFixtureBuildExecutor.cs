using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

/// <summary>Runs the controlled fixture command for target isolation and cancellation tests.</summary>
internal sealed class RunnerDeploymentFixtureBuildExecutor : IRunnerApplicationBuildExecutor
{
    public static DeployAppPackageRequest Request(Guid appId, Guid targetId, string package, Guid? attempt = null)
        => new() { AppId = appId, DeploymentTargetId = targetId, PackagePath = package,
            Clean = true, ExecutionAttemptId = attempt,
            ActionContainerPolicy = new(new(1m, 256, 128), 64, 10, 3, "test/actions:1") };

    public static string FixturePath() => Path.GetFullPath(Path.Combine(
        NUnit.Framework.TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..",
        "RunnerRuntimeFixture", "bin", "Debug", "net9.0", "RunnerRuntimeFixture.dll"));

    public Task<ApplicationBuildExecutionResult> ExecuteAsync(ApplicationBuildSpec build,
        ApplicationRuntimeSpec runtime, string workspaceRoot, RunnerApplicationBuildContext? context = null,
        CancellationToken cancellationToken = default)
        => ApplicationBuildExecutor.ExecuteAsync(build, workspaceRoot, cancellationToken);
}
