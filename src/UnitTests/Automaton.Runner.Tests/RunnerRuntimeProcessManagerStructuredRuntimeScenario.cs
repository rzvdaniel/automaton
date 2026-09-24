using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.RunnerRuntimeProcessManagerTestSupport;

namespace Automaton.Runner.Console.Tests;

internal static class RunnerRuntimeProcessManagerStructuredRuntimeScenario
{
    public static async Task RunAsync()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(workspace, registry);

        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "first", "second"]));
        await Task.Delay(150);
        await VerifyRuntimeOutputAsync(workspace, registry, appId, targetId);
        await manager.StopAsync(appId, targetId);
        Assert.That(registry.TryGet(appId, targetId, out _), Is.False);
    }

    private static async Task VerifyRuntimeOutputAsync(
        RunnerWorkspaceService workspace,
        RunnerRuntimeRegistry registry,
        Guid appId,
        Guid targetId)
    {
        var metadataPath = Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json");
        var metadata = JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            await File.ReadAllTextAsync(metadataPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var logsRoot = workspace.GetAppRuntimeLogsDirectory(appId, targetId, metadata.ArtifactId!);
        var stdout = await ReadSharedTextAsync(Path.Combine(logsRoot, "runtime.stdout.log"));
        var stderr = await ReadSharedTextAsync(Path.Combine(logsRoot, "runtime.stderr.log"));

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGet(appId, targetId, out var registration), Is.True);
            Assert.That(registration.Port, Is.EqualTo(metadata.EffectivePort));
            Assert.That(stdout, Does.Contain("stdout:--tcp|first|second:expected"));
            Assert.That(stderr, Does.Contain("stderr:fixture"));
            Assert.That(metadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(metadata.ProcessId, Is.Not.Null);
            Assert.That(File.ReadAllText(metadataPath), Does.Not.Contain("backendModule"));
        });
    }
}
