using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

/// <summary>After a Runner restart, the container of an Action that was running must be removed, even though the Runner's identity is only set a moment after startup.</summary>
[TestFixture, NonParallelizable]
public sealed class InterruptedActionContainerCleanupTests
{
    [Test]
    public async Task ACleanupBeforeTheRunnerKnowsItsIdentityIsRetriedLater_NotMarkedDone()
    {
        using var environment = MachineContainerTestEnvironment.Create();
        var root = Path.Combine(Path.GetTempPath(), "automaton-interrupted-action", Guid.NewGuid().ToString("N"));
        var priorRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        try
        {
            var workspace = new RunnerWorkspaceService();
            Guid app = Guid.NewGuid(), target = Guid.NewGuid(), execution = Guid.NewGuid();
            var statePath = Path.Combine(workspace.GetAppActionExecutionsDirectory(app, target, "work"),
                execution.ToString("N"), ".automaton-execution.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, JsonSerializer.Serialize(new
            {
                Terminal = false,
                Result = new AppActionExecutionResult
                {
                    ExecutionId = execution, Status = AppModuleExecutionStatuses.Running, StartedAtUtc = DateTime.UtcNow
                },
                AppId = app, DeploymentTargetId = target, ActionKey = "work"
            }));
            var docker = new MachineContainerDockerExecutor(environment.InstallationId);
            var service = new AppActionExecutionService(workspace, null,
                new MachineActionExecutionHandler(new HostContainerExecutionService(docker.ExecuteAsync)));
            var containerName = $"automaton-action-{environment.RunnerId:N}-{execution:N}";

            // Startup: the machine profile is known but the Runner identity is not set yet.
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_ID", null);
            await service.CleanupInterruptedActionContainersAsync(CancellationToken.None);
            var stateAfterEarlyAttempt = File.ReadAllText(statePath);

            // A moment later the identity is applied and the cleanup is attempted again.
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_ID", environment.RunnerId.ToString());
            await service.CleanupInterruptedActionContainersAsync(CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(stateAfterEarlyAttempt, Does.Contain("\"Terminal\":false"),
                    "an attempt that could not remove the container must not mark the execution as finished");
                Assert.That(docker.Commands.Any(command => command.Contains("rm") && command.Contains(containerName)), Is.True,
                    "the container is removed once the Runner knows its identity");
                Assert.That(File.ReadAllText(statePath), Does.Contain("\"Terminal\":true"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", priorRoot);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task AnExecutionStartedByThisRunnerIsNeverTreatedAsInterrupted()
    {
        using var environment = MachineContainerTestEnvironment.Create();
        var root = Path.Combine(Path.GetTempPath(), "automaton-interrupted-action", Guid.NewGuid().ToString("N"));
        var priorRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        try
        {
            var workspace = new RunnerWorkspaceService();
            var docker = new MachineContainerDockerExecutor(environment.InstallationId);
            var service = new AppActionExecutionService(workspace, null,
                new MachineActionExecutionHandler(new HostContainerExecutionService(docker.ExecuteAsync)));
            await Task.Delay(50);
            // A state written after the service started: a running execution of this very process.
            Guid app = Guid.NewGuid(), target = Guid.NewGuid(), execution = Guid.NewGuid();
            var statePath = Path.Combine(workspace.GetAppActionExecutionsDirectory(app, target, "work"),
                execution.ToString("N"), ".automaton-execution.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(statePath, JsonSerializer.Serialize(new
            {
                Terminal = false,
                Result = new AppActionExecutionResult
                {
                    ExecutionId = execution, Status = AppModuleExecutionStatuses.Running, StartedAtUtc = DateTime.UtcNow
                },
                AppId = app, DeploymentTargetId = target, ActionKey = "work"
            }));

            var complete = await service.CleanupInterruptedActionContainersAsync(CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(complete, Is.True);
                Assert.That(docker.Commands.Any(command => command.Contains("rm")), Is.False, "no container is removed");
                Assert.That(File.ReadAllText(statePath), Does.Contain("\"Terminal\":false"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", priorRoot);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
