using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging.Events;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class TrustedInfrastructureActionExecutorTests
{
    [TestCase("suspend-hosted-runner")]
    [TestCase("resume-hosted-runner")]
    public void RetiredStorageCommandIsRejectedBeforePreparationOrSecrets(string actionKey)
    {
        var secrets = new TerminalSecretProvider();
        var executor = new TrustedInfrastructureActionExecutor(null!, null!, secrets);
        var command = Command(actionKey) with { SecretGrantIds = [Guid.NewGuid()] };

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => executor.ExecuteAsync(command, CancellationToken.None));
        Assert.That(secrets.Calls, Is.Zero);
    }
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
        var result = await TrustedInfrastructureGuardScenario.RunAsync();

        Assert.That(result.ExecutionId, Is.EqualTo(result.CommandId));
        Assert.That(result.Status, Is.EqualTo(AppModuleExecutionStatuses.Failed));
        Assert.That(result.FailureSummary, Does.Contain("containerName contains unsupported characters"));
        Assert.That(result.DeployedEntrypointMatches, Is.True);
        Assert.That(result.ActionIsListed, Is.True);
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
            var execution = new AppActionExecutionService(workspace, coordinator);
            var secrets = new TerminalSecretProvider();
            var executor = CreateExecutor(workspace, coordinator, execution, secrets);
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

    private static TrustedInfrastructureActionExecutor CreateExecutor(
        RunnerWorkspaceService workspace,
        AppActionExecutionCoordinator coordinator,
        AppActionExecutionService execution,
        TerminalSecretProvider secrets)
        => new(new AppActionDeploymentService(workspace, coordinator), execution, secrets);

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
