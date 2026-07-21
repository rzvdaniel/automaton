using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class AppActionExecutionServiceTests
{
    [Test]
    public async Task OversizedStdoutAndStderrAreDrainedAndClassifiedWithoutDeadlock()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("This process-pipe integration test requires /bin/bash.");

        var original = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "automaton-action-streams", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        try
        {
            var workspace = new RunnerWorkspaceService();
            var appId = Guid.NewGuid();
            var actionDirectory = workspace.GetAppActionCurrentDirectory(appId, "noisy");
            Directory.CreateDirectory(actionDirectory);
            await File.WriteAllTextAsync(Path.Combine(actionDirectory, "action.yaml"), """
                apiVersion: automaton.dev/v1
                kind: Action
                metadata:
                  key: noisy
                spec:
                  runtime: bash
                  entrypoint: main.sh
                  timeoutSeconds: 30
                """);
            await File.WriteAllTextAsync(Path.Combine(actionDirectory, "main.sh"), """
                #!/usr/bin/env bash
                head -c 2097152 /dev/zero | tr '\0' 'x'
                head -c 2097152 /dev/zero | tr '\0' 'x' >&2
                """);
            await File.WriteAllTextAsync(Path.Combine(actionDirectory, ".automaton-action-deployment.json"), "{\"contentHash\":\"test\"}");

            using var input = System.Text.Json.JsonDocument.Parse("{}");
            var service = new AppActionExecutionService(workspace);
            var result = await service.ExecuteAsync(new Automaton.Core.Models.ExecuteAppActionModuleRequest
            {
                ExecutionId = Guid.NewGuid(), AppId = appId, ActionKey = "noisy", Input = input.RootElement.Clone()
            });

            Assert.That(result.Status, Is.EqualTo(Automaton.Core.Models.AppModuleExecutionStatuses.Failed));
            Assert.That(result.ErrorCode, Is.EqualTo(Automaton.Core.Models.AppModuleExecutionErrorCodes.ResultTooLarge));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", original);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void CleanupExpiredArtifacts_RemovesOnlyExpiredActionExecutions()
    {
        var original = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "automaton-action-cleanup", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        try
        {
            var workspace = new RunnerWorkspaceService();
            var service = new AppActionExecutionService(workspace);
            var appId = Guid.NewGuid();
            var expired = Path.Combine(workspace.GetAppActionExecutionsDirectory(appId, "hello"), Guid.NewGuid().ToString("N"));
            var current = Path.Combine(workspace.GetAppActionExecutionsDirectory(appId, "hello"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(expired);
            Directory.CreateDirectory(current);
            Directory.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-2));

            var removed = service.CleanupExpiredArtifacts();

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(Directory.Exists(expired), Is.False);
            Assert.That(Directory.Exists(current), Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", original);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
