using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging.Events;

namespace Automaton.Runner.Console.Services;

public interface IInfrastructureActionExecutor
{
    Task<(AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result)> ExecuteAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken);
}

public sealed class TrustedInfrastructureActionExecutor(
    AppActionDeploymentService deployment,
    AppActionExecutionService execution) : IInfrastructureActionExecutor
{
    public async Task<(AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result)> ExecuteAsync(
        InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken)
    {
        TrustedInfrastructureActions.EnsureAllowed(command.ActionKey);
        if (command.SecretGrantIds.Count != 0 || command.ActionKey is "provision-hosted-runner" or "reprovision-hosted-runner")
            throw new NotSupportedException("Infrastructure command secret grants remain unresolved until Stage 3.");
        var module = await deployment.PrepareBundledInfrastructureAsync(command.RunnerHostId, command.ActionKey, cancellationToken);
        using var input = JsonDocument.Parse(command.InputJson);
        var result = await execution.ExecuteAsync(new ExecuteAppActionModuleRequest
        {
            ExecutionId = command.CommandId,
            AppId = InfrastructureAppTrust.PlatformAppId,
            DeploymentTargetId = command.RunnerHostId,
            ActionKey = command.ActionKey,
            ExpectedDeploymentHash = module.ContentHash,
            SystemInfrastructure = true,
            Input = input.RootElement.Clone()
        }, cancellationToken);
        return (result, execution.ReadResult(new ReadAppModuleExecutionArtifactRequest(
            InfrastructureAppTrust.PlatformAppId, command.RunnerHostId, command.ActionKey, command.CommandId)));
    }
}
