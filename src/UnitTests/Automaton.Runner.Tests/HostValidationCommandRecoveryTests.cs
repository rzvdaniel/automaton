using System.Text.Json;
using NUnit.Framework;
using Automaton.Core.Models;
using Automaton.Messaging.Events;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class HostValidationCommandRecoveryTests
{
    [TestCase(true, AppModuleExecutionStatuses.Succeeded)]
    [TestCase(false, AppModuleExecutionStatuses.Lost)]
    public async Task RedeliveryReusesJournalWithoutExecutingHostProbe(bool terminal, string expectedStatus)
    {
        var previousRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var previousPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var root = Path.Combine(Path.GetTempPath(), "validation-replay-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", "Infrastructure");
        try
        {
            await AssertReplayAsync(terminal, expectedStatus);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", previousRoot);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", previousPurpose);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertReplayAsync(bool terminal, string expectedStatus)
    {
        var operationId = Guid.NewGuid();
        var command = new InfrastructureCommandRequestedEvent(operationId, operationId,
            Guid.NewGuid(), Guid.NewGuid(), HostValidationCommand.ActionKey,
            "{\"dockerNetwork\":\"never-probe-this-network\"}", [], DateTime.UtcNow);
        var workspace = new RunnerWorkspaceService();
        var directory = Path.Combine(workspace.GetAppActionExecutionsDirectory(
            InfrastructureAppTrust.PlatformAppId, command.RunnerHostId, command.ActionKey), operationId.ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, ".automaton-execution.json"), JsonSerializer.Serialize(new
        {
            Terminal = terminal,
            Result = new AppActionExecutionResult
            {
                ExecutionId = operationId,
                Status = terminal ? AppModuleExecutionStatuses.Succeeded : AppModuleExecutionStatuses.Running,
                StartedAtUtc = DateTime.UtcNow
            }
        }));
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), "{\"health\":{}}");
        var consumer = CreateConsumer(workspace, command);
        var first = await consumer.HandleAsync(JsonSerializer.SerializeToUtf8Bytes(command));
        var second = await consumer.HandleAsync(JsonSerializer.SerializeToUtf8Bytes(command));
        Assert.That(first.Execution!.Status, Is.EqualTo(expectedStatus));
        Assert.That(second.Execution!.Status, Is.EqualTo(expectedStatus));
        Assert.That(first.Execution.ExecutionId, Is.EqualTo(operationId));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(directory, "result.json")), Is.EqualTo("{\"health\":{}}"));
    }
    private static InfrastructureCommandConsumer CreateConsumer(RunnerWorkspaceService workspace,
        InfrastructureCommandRequestedEvent command)
    {
        var coordinator = new AppActionExecutionCoordinator();
        var executor = new TrustedInfrastructureActionExecutor(
            new AppActionDeploymentService(workspace, coordinator),
            new AppActionExecutionService(workspace, coordinator));
        return new InfrastructureCommandConsumer(new(command.RunnerHostId, command.InfrastructureRunnerId), executor);
    }

}
