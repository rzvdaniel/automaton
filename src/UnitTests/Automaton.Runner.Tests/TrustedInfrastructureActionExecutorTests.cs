using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging.Events;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class TrustedInfrastructureActionExecutorTests
{
    [Test]
    public void AllAllowlistedActionsAreBundledWithTheirExistingEntrypoints()
    {
        foreach (var key in TrustedInfrastructureActions.Keys)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "SystemApps", "automaton-infrastructure", "actions", key);
            var manifest = AppActionManifestParser.ParseFile(Path.Combine(directory, "action.yaml"), key, allowPrivilegedCapabilities: true);
            Assert.That(File.Exists(Path.Combine(directory, manifest.Spec.Entrypoint)), Is.True, key);
        }
    }

    [Test]
    public async Task ExistingPythonActionGuardRunsThroughLocalPreparationAndExecution()
    {
        var originalRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var originalPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var root = Path.Combine(Path.GetTempPath(), "ic-" + Guid.NewGuid().ToString("N")[..8]);
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", "Infrastructure");
        try
        {
            var workspace = new RunnerWorkspaceService();
            var coordinator = new AppActionExecutionCoordinator();
            var deployment = new AppActionDeploymentService(workspace, coordinator);
            var executor = new TrustedInfrastructureActionExecutor(deployment, new AppActionExecutionService(workspace, coordinator));
            var command = Command("restart-hosted-runner");
            command = command with { InputJson = JsonSerializer.Serialize(new
            {
                operationId = command.OperationId, hostedRunnerId = Guid.NewGuid(), containerName = "invalid/name"
            }) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var result = await new InfrastructureCommandConsumer(new(command.RunnerHostId, command.InfrastructureRunnerId), executor)
                .HandleAsync(JsonSerializer.SerializeToUtf8Bytes(command), timeout.Token);

            Assert.That(result.Execution!.ExecutionId, Is.EqualTo(command.CommandId));
            Assert.That(result.Execution.Status, Is.EqualTo(AppModuleExecutionStatuses.Failed));
            Assert.That(result.Execution.FailureSummary, Does.Contain("containerName contains unsupported characters"));
            var current = workspace.GetAppActionCurrentDirectory(InfrastructureAppTrust.PlatformAppId, command.RunnerHostId, command.ActionKey);
            Assert.That(File.ReadAllText(Path.Combine(current, "main.py")), Is.EqualTo(File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "SystemApps", "automaton-infrastructure", "actions", command.ActionKey, "main.py"))));
            Assert.That(deployment.ListModules(InfrastructureAppTrust.PlatformAppId, command.RunnerHostId).Actions.Single().Key,
                Is.EqualTo(command.ActionKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalRoot);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", originalPurpose);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task TerminalSecretResolutionDoesNotExecuteTrustedAction()
    {
        var originalRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var originalPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var root = Path.Combine(Path.GetTempPath(), "ic-terminal-" + Guid.NewGuid().ToString("N")[..8]);
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", "Infrastructure");
        try
        {
            var workspace = new RunnerWorkspaceService();
            var coordinator = new AppActionExecutionCoordinator();
            var deployment = new AppActionDeploymentService(workspace, coordinator);
            var execution = new AppActionExecutionService(workspace, coordinator);
            var secrets = new TerminalSecretProvider();
            var executor = new TrustedInfrastructureActionExecutor(deployment, execution, secrets);
            var command = Command("restart-hosted-runner") with { SecretGrantIds = [Guid.NewGuid()] };

            var result = await executor.ExecuteAsync(command, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Terminal, Is.True);
                Assert.That(result.TerminalReason, Is.EqualTo("AmbiguousDispatch"));
                Assert.That(result.Execution, Is.Null);
                Assert.That(secrets.Calls, Is.EqualTo(1));
            });
            Assert.That(execution.ReadPersistedResult(new ReadAppModuleExecutionArtifactRequest(
                InfrastructureAppTrust.PlatformAppId, command.RunnerHostId, command.ActionKey, command.CommandId)), Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalRoot);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", originalPurpose);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static InfrastructureCommandRequestedEvent Command(string actionKey)
    {
        var operationId = Guid.NewGuid();
        return new(operationId, operationId, Guid.NewGuid(), Guid.NewGuid(), actionKey, "{}", [], DateTime.UtcNow);
    }

    private sealed class TerminalSecretProvider : IInfrastructureSecretProvider
    {
        public int Calls { get; private set; }

        public Task<InfrastructureCommandSecretsResponse> ResolveAsync(
            InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new InfrastructureCommandSecretsResponse(
                InfrastructureCommandSecretResolutionDispositions.Terminal,
                new Dictionary<string, string>(), "AmbiguousDispatch"));
        }
    }
}
