using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging.Events;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

internal static class TrustedInfrastructureGuardScenario
{
    public static async Task<TrustedInfrastructureGuardOutcome> RunAsync()
    {
        var originalRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var originalPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var root = Path.Combine(Path.GetTempPath(), "ic-" + Guid.NewGuid().ToString("N")[..8]);
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", "Infrastructure");
        try
        {
            var setup = CreateExecutor();
            var command = CreateInvalidHostedRunnerCommand();
            var result = await ExecuteAsync(command, setup.Executor);
            var current = setup.Workspace.GetAppActionCurrentDirectory(
                InfrastructureAppTrust.PlatformAppId,
                command.RunnerHostId,
                command.ActionKey);
            var bundledEntrypoint = Path.Combine(
                AppContext.BaseDirectory,
                "SystemApps",
                "automaton-infrastructure",
                "actions",
                command.ActionKey,
                "main.py");
            var deployedEntrypoint = Path.Combine(current, "main.py");

            return new(
                result.Execution!.ExecutionId,
                command.CommandId,
                result.Execution.Status,
                result.Execution.FailureSummary,
                File.ReadAllText(deployedEntrypoint) == File.ReadAllText(bundledEntrypoint),
                setup.Deployment.ListModules(InfrastructureAppTrust.PlatformAppId, command.RunnerHostId)
                    .Actions.Single().Key == command.ActionKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalRoot);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", originalPurpose);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static (RunnerWorkspaceService Workspace, AppActionDeploymentService Deployment, TrustedInfrastructureActionExecutor Executor)
        CreateExecutor()
    {
        var workspace = new RunnerWorkspaceService();
        var coordinator = new AppActionExecutionCoordinator();
        var deployment = new AppActionDeploymentService(workspace, coordinator);
        var execution = new AppActionExecutionService(workspace, coordinator);
        return (workspace, deployment, new TrustedInfrastructureActionExecutor(deployment, execution));
    }

    private static InfrastructureCommandRequestedEvent CreateInvalidHostedRunnerCommand()
    {
        var command = Command("restart-hosted-runner");
        return command with
        {
            InputJson = JsonSerializer.Serialize(new
            {
                operationId = command.OperationId,
                hostedRunnerId = Guid.NewGuid(),
                containerName = "invalid/name"
            })
        };
    }

    private static async Task<InfrastructureCommandExecutionResult> ExecuteAsync(
        InfrastructureCommandRequestedEvent command,
        TrustedInfrastructureActionExecutor executor)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var consumer = new InfrastructureCommandConsumer(
            new(command.RunnerHostId, command.InfrastructureRunnerId),
            executor);
        return await consumer.HandleAsync(JsonSerializer.SerializeToUtf8Bytes(command), timeout.Token);
    }

    private static InfrastructureCommandRequestedEvent Command(string actionKey)
    {
        var operationId = Guid.NewGuid();
        return new(operationId, operationId, Guid.NewGuid(), Guid.NewGuid(), actionKey, "{}", [], DateTime.UtcNow);
    }
}

internal sealed record TrustedInfrastructureGuardOutcome(
    Guid ExecutionId,
    Guid CommandId,
    string Status,
    string? FailureSummary,
    bool DeployedEntrypointMatches,
    bool ActionIsListed);
