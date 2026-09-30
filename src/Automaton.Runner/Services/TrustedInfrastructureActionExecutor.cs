using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging.Events;

namespace Automaton.Runner.Console.Services;

public interface IInfrastructureActionExecutor
{
    Task<InfrastructureCommandExecutionResult> ExecuteAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken);
}

public interface IInfrastructureSecretProvider
{
    Task<InfrastructureCommandSecretsResponse> ResolveAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken);
}

public sealed record InfrastructureCommandExecutionResult(
    AppActionExecutionResult? Execution,
    AppModuleExecutionArtifactResponse? Result,
    bool Terminal,
    string? TerminalReason,
    HostContainerExecutionResponse? HostContainerResult = null);

public sealed class TrustedInfrastructureActionExecutor(
    AppActionDeploymentService deployment,
    AppActionExecutionService execution,
    IInfrastructureSecretProvider secrets) : IInfrastructureActionExecutor
{
    public TrustedInfrastructureActionExecutor(AppActionDeploymentService deployment, AppActionExecutionService execution)
        : this(deployment, execution, new MissingInfrastructureSecretProvider())
    {
    }
    public async Task<InfrastructureCommandExecutionResult> ExecuteAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken)
    {
        TrustedInfrastructureActions.EnsureAllowed(command.ActionKey);
        var artifactRequest = new ReadAppModuleExecutionArtifactRequest(
            InfrastructureAppTrust.PlatformAppId, command.RunnerHostId, command.ActionKey, command.CommandId);
        var existing = execution.ReadPersistedResult(artifactRequest);
        if (existing != null)
            return await ReconcileExistingAsync(command, artifactRequest, existing, cancellationToken);

        return await ExecuteNewAsync(command, artifactRequest, cancellationToken);
    }

    private async Task<InfrastructureCommandExecutionResult> ReconcileExistingAsync(
        InfrastructureCommandRequestedEvent command,
        ReadAppModuleExecutionArtifactRequest artifactRequest,
        AppActionExecutionResult existing,
        CancellationToken cancellationToken)
    {
        if (existing.Status != AppModuleExecutionStatuses.Running)
            return new(existing, execution.ReadResult(artifactRequest), false, null);

        var module = await deployment.PrepareBundledInfrastructureAsync(command.RunnerHostId, command.ActionKey, cancellationToken);
        var recovered = await execution.ExecuteAsync(new ExecuteAppActionModuleRequest
        {
            ExecutionId = command.CommandId, AppId = InfrastructureAppTrust.PlatformAppId,
            DeploymentTargetId = command.RunnerHostId, ActionKey = command.ActionKey,
            ExpectedDeploymentHash = module.ContentHash, SystemInfrastructure = true
        }, cancellationToken);
        return new(recovered, execution.ReadResult(artifactRequest), false, null);
    }

    private async Task<InfrastructureCommandExecutionResult> ExecuteNewAsync(
        InfrastructureCommandRequestedEvent command,
        ReadAppModuleExecutionArtifactRequest artifactRequest,
        CancellationToken cancellationToken)
    {
        var secretResolution = command.SecretGrantIds.Count == 0
            ? new InfrastructureCommandSecretsResponse(InfrastructureCommandSecretResolutionDispositions.Executable,
                new Dictionary<string, string>())
            : await secrets.ResolveAsync(command, cancellationToken);
        if (secretResolution.Disposition == InfrastructureCommandSecretResolutionDispositions.Terminal)
            return new(null, null, true, secretResolution.TerminalReason);
        if (secretResolution.Disposition != InfrastructureCommandSecretResolutionDispositions.Executable)
            throw new InvalidOperationException("The Infrastructure API returned an unknown secret resolution disposition.");
        var preparedModule = await deployment.PrepareBundledInfrastructureAsync(command.RunnerHostId, command.ActionKey, cancellationToken);
        using var input = JsonDocument.Parse(command.InputJson);
        var result = await execution.ExecuteAsync(new ExecuteAppActionModuleRequest
        {
            ExecutionId = command.CommandId,
            AppId = InfrastructureAppTrust.PlatformAppId,
            DeploymentTargetId = command.RunnerHostId,
            ActionKey = command.ActionKey,
            ExpectedDeploymentHash = preparedModule.ContentHash,
            SystemInfrastructure = true,
            Input = input.RootElement.Clone(),
            Secrets = new Dictionary<string, string>(secretResolution.EnvironmentVariables, StringComparer.Ordinal)
        }, cancellationToken);
        return new(result, execution.ReadResult(artifactRequest), false, null);
    }

    private sealed class MissingInfrastructureSecretProvider : IInfrastructureSecretProvider
    {
        public Task<InfrastructureCommandSecretsResponse> ResolveAsync(
            InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Infrastructure command secret delivery requires the Stage 3 API boundary.");
    }
}
