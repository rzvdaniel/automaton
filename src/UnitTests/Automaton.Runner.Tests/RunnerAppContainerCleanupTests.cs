using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
namespace Automaton.Runner.Console.Tests;
[TestFixture, NonParallelizable]
public sealed class RunnerAppContainerCleanupTests
{
    private string root = null!;
    private string? previous;
    [SetUp] public void SetUp()
    {
        previous = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        root = Path.Combine(Path.GetTempPath(), "app-cleanup-" + Guid.NewGuid());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
    }
    [TearDown] public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", previous);
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task StopWithoutMetadata_DiscoversOrphanAndRejectsWrongOwner(bool wrongOwner)
    {
        var app = Guid.NewGuid();
        var target = Guid.NewGuid();
        var id = new string('a', 64);
        var labels = new Dictionary<string, string> { ["com.automaton.managed"] = "true",
            ["com.automaton.runtime-kind"] = "generic", ["com.automaton.app-id"] = (wrongOwner ? Guid.NewGuid() : app).ToString(),
            ["com.automaton.deployment-target-id"] = target.ToString() };
        var calls = new List<IReadOnlyList<string>>();
        var removed = false;
        var runtime = new DockerContainerRuntime((args, _, _, _) =>
        {
            calls.Add(args.ToArray());
            if (args[0] == "ps") return Task.FromResult(new DockerContainerRuntime.CommandResult(0, removed ? "" : id, ""));
            if (args[0] == "rm") { removed = true; return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "", "")); }
            return Task.FromResult(new DockerContainerRuntime.CommandResult(0, id + "|true|" + JsonSerializer.Serialize(labels), ""));
        });
        var manager = new RunnerRuntimeContainerManager(new RunnerWorkspaceService(), new RunnerRuntimeRegistry(),
            runtime, NullLogger<RunnerRuntimeContainerManager>.Instance);
        if (wrongOwner) Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.StopAsync(app, target));
        else { await manager.StopAsync(app, target); await manager.StopAsync(app, target); }
        Assert.That(removed, Is.EqualTo(!wrongOwner));
        Assert.That(calls[0], Does.Contain($"label=com.automaton.app-id={app}"));
        Assert.That(calls[0], Does.Contain($"label=com.automaton.deployment-target-id={target}"));
    }
}
