using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging.Events;

namespace Automaton.Runner.Console.Services;

public interface IInfrastructureActionExecutor
{
    Task<InfrastructureCommandExecutionResult> ExecuteAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken);
}

public sealed record InfrastructureCommandExecutionResult(
    AppActionExecutionResult? Execution,
    AppModuleExecutionArtifactResponse? Result,
    bool Terminal,
    string? TerminalReason);

public sealed class TrustedInfrastructureActionExecutor(
    AppActionDeploymentService deployment,
    AppActionExecutionService execution) : IInfrastructureActionExecutor
{
    public async Task<InfrastructureCommandExecutionResult> ExecuteAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken)
    {
        TrustedInfrastructureActions.EnsureAllowed(command.ActionKey);
        if (command.SecretGrantIds.Count != 0)
            throw new UnauthorizedAccessException("Host validation does not accept secret grants.");
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
            Secrets = new Dictionary<string, string>()
        }, cancellationToken);
        return new(result, execution.ReadResult(artifactRequest), false, null);
    }

}
