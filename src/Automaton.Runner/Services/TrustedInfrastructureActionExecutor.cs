using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging.Events;

namespace Automaton.Runner.Console.Services;

public interface IInfrastructureActionExecutor
{
    Task<(AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result)> ExecuteAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken);
}

public interface IInfrastructureSecretProvider
{
    Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken);
}

public sealed class TrustedInfrastructureActionExecutor(
    AppActionDeploymentService deployment,
    AppActionExecutionService execution,
    IInfrastructureSecretProvider secrets) : IInfrastructureActionExecutor
{
    public TrustedInfrastructureActionExecutor(AppActionDeploymentService deployment, AppActionExecutionService execution)
        : this(deployment, execution, new MissingInfrastructureSecretProvider())
    {
    }
    public async Task<(AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result)> ExecuteAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken)
    {
        TrustedInfrastructureActions.EnsureAllowed(command.ActionKey);
        var module = await deployment.PrepareBundledInfrastructureAsync(command.RunnerHostId, command.ActionKey, cancellationToken);
        var artifactRequest = new ReadAppModuleExecutionArtifactRequest(
            InfrastructureAppTrust.PlatformAppId, command.RunnerHostId, command.ActionKey, command.CommandId);
        var existing = execution.ReadPersistedResult(artifactRequest);
        if (existing != null)
        {
            if (existing.Status == AppModuleExecutionStatuses.Running)
                return (await execution.ExecuteAsync(new ExecuteAppActionModuleRequest
                {
                    ExecutionId = command.CommandId, AppId = InfrastructureAppTrust.PlatformAppId,
                    DeploymentTargetId = command.RunnerHostId, ActionKey = command.ActionKey,
                    ExpectedDeploymentHash = module.ContentHash, SystemInfrastructure = true
                }, cancellationToken), execution.ReadResult(artifactRequest));
            return (existing, execution.ReadResult(artifactRequest));
        }
        IReadOnlyDictionary<string, string> resolvedSecrets = command.SecretGrantIds.Count == 0
            ? new Dictionary<string, string>()
            : await secrets.ResolveAsync(command, cancellationToken);
        using var input = JsonDocument.Parse(command.InputJson);
        var result = await execution.ExecuteAsync(new ExecuteAppActionModuleRequest
        {
            ExecutionId = command.CommandId,
            AppId = InfrastructureAppTrust.PlatformAppId,
            DeploymentTargetId = command.RunnerHostId,
            ActionKey = command.ActionKey,
            ExpectedDeploymentHash = module.ContentHash,
            SystemInfrastructure = true,
            Input = input.RootElement.Clone(),
            Secrets = new Dictionary<string, string>(resolvedSecrets, StringComparer.Ordinal)
        }, cancellationToken);
        return (result, execution.ReadResult(artifactRequest));
    }

    private sealed class MissingInfrastructureSecretProvider : IInfrastructureSecretProvider
    {
        public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Infrastructure command secret delivery requires the Stage 3 API boundary.");
    }
}
