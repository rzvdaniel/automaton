using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.RunnerRuntimeProcessManagerTestSupport;

namespace Automaton.Runner.Console.Tests;

internal static class RunnerRuntimeProcessManagerFailedReplacementScenario
{
    public static async Task RunAsync()
    {
        var context = await StartOriginalRuntimeAsync();
        await AssertReplacementFailsAsync(context);
        RunnerRuntimeProcessManagerReplacementAssertions.AssertFailedReplacementWasRolledBack(
            context.MetadataPath,
            context.Registry,
            context.AppId,
            context.TargetId,
            context.Original,
            context.OriginalPort);
        await context.Manager.StopAsync(context.AppId, context.TargetId);
        Assert.That(await WaitForProcessExitAsync(context.Original.ProcessId!.Value), Is.True);
    }

    private static async Task<ScenarioContext> StartOriginalRuntimeAsync()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var manager = CreateManager(workspace, registry);
        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "original"]));

        var metadataPath = Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json");
        var original = ReadMetadata(metadataPath);
        Assert.That(original.ProcessId, Is.Not.Null);
        Assert.That(registry.TryGet(appId, targetId, out var originalRoute), Is.True);

        return new ScenarioContext(manager, registry, appId, targetId, metadataPath, original, originalRoute.Port);
    }

    private static async Task AssertReplacementFailsAsync(ScenarioContext context)
    {
        var replacement = CreateContract([]);
        replacement.Runtime.Command!.Arguments = ["missing-runtime-fixture.dll"];
        replacement.Health.StartupTimeoutSeconds = 1;
        Assert.That(
            async () => await context.Manager.ApplyAsync(context.AppId, context.TargetId, replacement),
            Throws.TypeOf<TimeoutException>());
    }

    private sealed record ScenarioContext(
        RunnerRuntimeProcessManager Manager,
        RunnerRuntimeRegistry Registry,
        Guid AppId,
        Guid TargetId,
        string MetadataPath,
        RunnerRuntimeDeploymentMetadata Original,
        int OriginalPort);
}
