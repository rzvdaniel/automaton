using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

internal static class InjectedSecretRedactionScenario
{
    private const string Secret = "re_sensitive-value-with-specials";

    public static async Task<InjectedSecretRedactionOutcome> RunAsync()
    {
        var originalRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "automaton-action-redaction", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        try
        {
            var workspace = new RunnerWorkspaceService();
            var appId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var executionId = Guid.NewGuid();
            await CreateActionAsync(workspace, appId, targetId);
            return await ExecuteAndReadAsync(workspace, appId, targetId, executionId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalRoot);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateActionAsync(RunnerWorkspaceService workspace, Guid appId, Guid targetId)
    {
        var actionDirectory = workspace.GetAppActionCurrentDirectory(appId, targetId, "echo-secret");
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
        await File.WriteAllTextAsync(
            Path.Combine(actionDirectory, ".automaton-action-deployment.json"),
            "{\"contentHash\":\"test\"}");
    }

    private static async Task<InjectedSecretRedactionOutcome> ExecuteAndReadAsync(
        RunnerWorkspaceService workspace,
        Guid appId,
        Guid targetId,
        Guid executionId)
    {
        var service = new AppActionExecutionService(workspace);
        var request = CreateRequest(appId, targetId, executionId);
        var result = await service.ExecuteAsync(request);
        var duplicate = await service.ExecuteAsync(request);
        var artifact = new ReadAppModuleExecutionArtifactRequest(appId, targetId, "echo-secret", executionId);
        return new(
            result.Status,
            duplicate.Status,
            result.StartedAtUtc,
            duplicate.StartedAtUtc,
            service.ReadLogs(artifact).Content,
            service.ReadResult(artifact).Content,
            result.FailureSummary,
            Secret);
    }

    private static ExecuteAppActionModuleRequest CreateRequest(Guid appId, Guid targetId, Guid executionId)
    {
        using var input = JsonDocument.Parse("{}");
        return new ExecuteAppActionModuleRequest
        {
            ExecutionId = executionId,
            AppId = appId,
            DeploymentTargetId = targetId,
            ActionKey = "echo-secret",
            Input = input.RootElement.Clone(),
            Secrets = new Dictionary<string, string> { ["DELIVERED_SECRET"] = Secret }
        };
    }
}

internal sealed record InjectedSecretRedactionOutcome(
    string Status,
    string DuplicateStatus,
    DateTime StartedAtUtc,
    DateTime DuplicateStartedAtUtc,
    string Logs,
    string Output,
    string? FailureSummary,
    string Secret);
