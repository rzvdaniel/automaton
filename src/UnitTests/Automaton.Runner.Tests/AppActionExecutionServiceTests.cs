using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class AppActionExecutionServiceTests
{
    [Test]
    public async Task DuplicateExecutionId_ReusesTerminalResultAndDoesNotRestartInterruptedExecution()
    {
        var original = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "automaton-action-idempotency", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        try
        {
            var workspace = new RunnerWorkspaceService();
            var service = new AppActionExecutionService(workspace);
            var appId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            const string actionKey = "idempotent";
            var terminalExecutionId = Guid.NewGuid();
            var terminalDirectory = Path.Combine(
                workspace.GetAppActionExecutionsDirectory(appId, targetId, actionKey),
                terminalExecutionId.ToString("N"));
            Directory.CreateDirectory(terminalDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(terminalDirectory, ".automaton-execution.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    Terminal = true,
                    Result = new Automaton.Core.Models.AppActionExecutionResult
                    {
                        ExecutionId = terminalExecutionId,
                        Status = Automaton.Core.Models.AppModuleExecutionStatuses.Succeeded,
                        StartedAtUtc = DateTime.UtcNow.AddSeconds(-1),
                        CompletedAtUtc = DateTime.UtcNow
                    }
                }));

            var terminal = await service.ExecuteAsync(new Automaton.Core.Models.ExecuteAppActionModuleRequest
            {
                ExecutionId = terminalExecutionId,
                AppId = appId,
                DeploymentTargetId = targetId,
                ActionKey = actionKey
            });

            Assert.That(terminal.Status, Is.EqualTo(Automaton.Core.Models.AppModuleExecutionStatuses.Succeeded));

            var interruptedExecutionId = Guid.NewGuid();
            var interruptedDirectory = Path.Combine(
                workspace.GetAppActionExecutionsDirectory(appId, targetId, actionKey),
                interruptedExecutionId.ToString("N"));
            Directory.CreateDirectory(interruptedDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(interruptedDirectory, ".automaton-execution.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    Terminal = false,
                    Result = new Automaton.Core.Models.AppActionExecutionResult
                    {
                        ExecutionId = interruptedExecutionId,
                        Status = Automaton.Core.Models.AppModuleExecutionStatuses.Running,
                        StartedAtUtc = DateTime.UtcNow
                    }
                }));

            var interrupted = await service.ExecuteAsync(new Automaton.Core.Models.ExecuteAppActionModuleRequest
            {
                ExecutionId = interruptedExecutionId,
                AppId = appId,
                DeploymentTargetId = targetId,
                ActionKey = actionKey
            });

            Assert.That(interrupted.Status, Is.EqualTo(Automaton.Core.Models.AppModuleExecutionStatuses.Lost));
            Assert.That(interrupted.ErrorCode, Is.EqualTo(Automaton.Core.Models.AppModuleExecutionErrorCodes.AmbiguousDispatch));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", original);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

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
            var targetId = Guid.NewGuid();
            var actionDirectory = workspace.GetAppActionCurrentDirectory(appId, targetId, "noisy");
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
                ExecutionId = Guid.NewGuid(), AppId = appId, DeploymentTargetId = targetId, ActionKey = "noisy", Input = input.RootElement.Clone()
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
            var targetId = Guid.NewGuid();
            var expired = Path.Combine(workspace.GetAppActionExecutionsDirectory(appId, targetId, "hello"), Guid.NewGuid().ToString("N"));
            var current = Path.Combine(workspace.GetAppActionExecutionsDirectory(appId, targetId, "hello"), Guid.NewGuid().ToString("N"));
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

    [Test]
    public async Task InjectedSecretsAreRedactedFromLogsResultsAndFailureMetadata()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("This process integration test requires /bin/bash.");

        var original = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "automaton-action-redaction", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        try
        {
            const string secret = "re_sensitive-value-with-specials";
            var workspace = new RunnerWorkspaceService();
            var appId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var actionKey = "echo-secret";
            var executionId = Guid.NewGuid();
            var actionDirectory = workspace.GetAppActionCurrentDirectory(appId, targetId, actionKey);
            Directory.CreateDirectory(actionDirectory);
            await File.WriteAllTextAsync(Path.Combine(actionDirectory, "action.yaml"), """
                apiVersion: automaton.dev/v1
                kind: Action
                metadata:
                  key: echo-secret
                spec:
                  runtime: bash
                  entrypoint: main.sh
                  timeoutSeconds: 30
                """);
            await File.WriteAllTextAsync(Path.Combine(actionDirectory, "main.sh"), """
                #!/usr/bin/env bash
                printf '%s' "$DELIVERED_SECRET" >&2
                printf '{"value":"%s"}' "$DELIVERED_SECRET"
                """);
            await File.WriteAllTextAsync(Path.Combine(actionDirectory, ".automaton-action-deployment.json"), "{\"contentHash\":\"test\"}");

            using var input = System.Text.Json.JsonDocument.Parse("{}");
            var service = new AppActionExecutionService(workspace);
            var request = new Automaton.Core.Models.ExecuteAppActionModuleRequest
            {
                ExecutionId = executionId,
                AppId = appId,
                DeploymentTargetId = targetId,
                ActionKey = actionKey,
                Input = input.RootElement.Clone(),
                Secrets = new Dictionary<string, string> { ["DELIVERED_SECRET"] = secret }
            };
            var result = await service.ExecuteAsync(request);
            var duplicateResult = await service.ExecuteAsync(request);

            Assert.That(result.Status, Is.EqualTo(Automaton.Core.Models.AppModuleExecutionStatuses.Succeeded));
            Assert.That(duplicateResult.Status, Is.EqualTo(result.Status));
            Assert.That(duplicateResult.StartedAtUtc, Is.EqualTo(result.StartedAtUtc));
            var logs = service.ReadLogs(new Automaton.Core.Models.ReadAppModuleExecutionArtifactRequest(appId, targetId, actionKey, executionId));
            var output = service.ReadResult(new Automaton.Core.Models.ReadAppModuleExecutionArtifactRequest(appId, targetId, actionKey, executionId));
            Assert.That(logs.Content, Does.Not.Contain(secret));
            Assert.That(logs.Content, Does.Contain("[REDACTED]"));
            Assert.That(output.Content, Does.Not.Contain(secret));
            Assert.That(output.Content, Does.Contain("[REDACTED]"));
            Assert.That(result.FailureSummary, Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", original);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
