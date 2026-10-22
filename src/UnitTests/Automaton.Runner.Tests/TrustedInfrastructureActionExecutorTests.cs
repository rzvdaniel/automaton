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
    [TestCase("measure-hosted-runner-storage")]
    [TestCase("restart-hosted-runner")]
    [TestCase("delete-hosted-runner")]
    public void RetiredStorageCommandIsRejectedBeforePreparationOrSecrets(string actionKey)
    {
        var executor = new TrustedInfrastructureActionExecutor(null!, null!);
        var command = Command(actionKey) with { SecretGrantIds = [Guid.NewGuid()] };

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => executor.ExecuteAsync(command, CancellationToken.None));
    }
    [Test]
    public void HostValidationRejectsSecretGrantsBeforePreparation()
    {
        var executor = new TrustedInfrastructureActionExecutor(null!, null!);
        var command = Command(HostValidationCommand.ActionKey) with { SecretGrantIds = [Guid.NewGuid()] };
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => executor.ExecuteAsync(command, CancellationToken.None));
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

    private static InfrastructureCommandRequestedEvent Command(string actionKey)
    {
        var operationId = Guid.NewGuid();
        return new(operationId, operationId, Guid.NewGuid(), Guid.NewGuid(), actionKey, "{}", [], DateTime.UtcNow);
    }

}
