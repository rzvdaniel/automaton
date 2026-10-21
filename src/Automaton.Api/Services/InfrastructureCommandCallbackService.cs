using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Automaton.Api.Services;

public sealed class InfrastructureCommandCallbackService(
    ApplicationDbContext db,
    IExecutionSecretGrantService secretGrants,
    InfrastructureActionProvisioningHandler lifecycle,
    InfrastructureCommandResultService resultService,
    ITransactionalEventExecutor? transactionalEventExecutor = null,
    ILogger<InfrastructureCommandCallbackService>? logger = null)
{
    public async Task<InfrastructureCommandSecretsResponse> ResolveSecretsAsync(Guid apiKeyId,
        Guid commandId, InfrastructureCommandSecretsRequest request, CancellationToken cancellationToken)
    {
        var scope = await AuthorizeAsync(apiKeyId, commandId, request.OperationId, request.RunnerHostId,
            request.InfrastructureRunnerId, request.ActionKey, cancellationToken);
        var deployment = await db.HostedRunnerDeployments.AsNoTracking()
            .SingleAsync(x => x.Id == request.OperationId, cancellationToken);
        if (IsTerminalDeployment(deployment.Status))
            return TerminalSecretResolution(deployment.Error ?? HostedRunnerDeploymentErrors.AmbiguousDispatch);

        try
        {
            var lease = await secretGrants.LeaseForInfrastructureCommandAsync(scope, request.SecretGrantIds, cancellationToken);
            return new(InfrastructureCommandSecretResolutionDispositions.Executable, lease.EnvironmentVariables);
        }
        catch (SecretGrantUnavailableException exception)
        {
            var terminalReason = await TerminalizeAsync(request.OperationId, request.ActionKey,
                exception.Reason, cancellationToken);
            logger?.LogWarning(
                "Infrastructure command {CommandId} for operation {OperationId} ({ActionKey}) was terminalized during secret resolution: {DispositionReason}; ambiguous={Ambiguous}",
                commandId, request.OperationId, request.ActionKey, terminalReason, exception.Ambiguous);
            return TerminalSecretResolution(terminalReason);
        }
    }

    private async Task<string> TerminalizeAsync(Guid operationId, string actionKey, string reason,
        CancellationToken cancellationToken)
    {
        if (transactionalEventExecutor != null)
        {
            await transactionalEventExecutor.ExecuteAsync(
                token => lifecycle.TerminalizeUnexecutableAsync(operationId, actionKey, reason, token), cancellationToken);
        }
        else
        {
            await lifecycle.TerminalizeUnexecutableAsync(operationId, actionKey, reason, cancellationToken);
        }

        db.ChangeTracker.Clear();
        var persisted = await db.HostedRunnerDeployments.AsNoTracking()
            .SingleAsync(x => x.Id == operationId, cancellationToken);
        if (!IsTerminalDeployment(persisted.Status) || persisted.FinishedAt is null
            || persisted.Status == HostedRunnerDeploymentStatuses.Failed && string.IsNullOrWhiteSpace(persisted.Error))
            throw new InvalidOperationException(
                "Infrastructure command terminalization could not be confirmed as durably persisted.");

        return persisted.Error ?? HostedRunnerDeploymentErrors.AmbiguousDispatch;
    }

    private static InfrastructureCommandSecretsResponse TerminalSecretResolution(string reason)
        => new(InfrastructureCommandSecretResolutionDispositions.Terminal, new Dictionary<string, string>(), reason);

    public async Task<InfrastructureCommandResultResponse> ApplyResultAsync(Guid apiKeyId,
        Guid commandId, InfrastructureCommandResultRequest request, CancellationToken cancellationToken)
    {
        _ = await AuthorizeAsync(apiKeyId, commandId, request.OperationId, request.RunnerHostId,
            request.InfrastructureRunnerId, request.ActionKey, cancellationToken);
        return await resultService.ApplyAsync(commandId, request, cancellationToken);
    }

    private async Task<SecretGrantScope> AuthorizeAsync(Guid apiKeyId, Guid commandId, Guid operationId,
        Guid runnerHostId, Guid runnerId, string actionKey, CancellationToken cancellationToken)
    {
        if (commandId != operationId) throw new UnauthorizedAccessException("Infrastructure command identity is invalid.");
        TrustedInfrastructureActions.EnsureAllowed(actionKey);
        var host = await db.RunnerHosts.Include(x => x.InfrastructureRunner)
            .SingleOrDefaultAsync(x => x.Id == runnerHostId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The Infrastructure Runner Host was not found.");
        var runner = host.InfrastructureRunner;
        if (runner == null || runner.Id != runnerId || runner.ApiKeyId != apiKeyId
            || !string.Equals(runner.Purpose, RunnerPurposes.Infrastructure, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The authenticated API key does not belong to the addressed Infrastructure Runner.");
        var deployment = await db.HostedRunnerDeployments.Include(x => x.HostedRunner)
            .SingleOrDefaultAsync(x => x.Id == operationId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The infrastructure command operation was not found.");
        if (deployment.HostedRunner.RunnerHostId != runnerHostId)
            throw new UnauthorizedAccessException("The infrastructure command operation is not assigned to this Runner Host.");
        var expectedActionKey = TrustedActionKeyForDeployment(deployment.Action)
            ?? throw new UnauthorizedAccessException("The infrastructure command operation has an unknown deployment action.");
        if (!string.Equals(expectedActionKey, actionKey, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The infrastructure command Action does not match the persisted operation action.");
        return new SecretGrantScope(operationId, InfrastructureAppTrust.PlatformAppId, actionKey, runnerId,
            runnerHostId, SecretOwnerTypes.HostedRunner, deployment.HostedRunnerId);
    }

    internal static string? TrustedActionKeyForDeployment(string action) => action switch
    {
        HostedRunnerDeploymentActions.Restart => "restart-hosted-runner",
        HostedRunnerDeploymentActions.Suspend => StorageEnforcementActions.SuspendHostedRunner,
        HostedRunnerDeploymentActions.Resume => StorageEnforcementActions.ResumeHostedRunner,
        HostedRunnerDeploymentActions.Deprovision => "delete-hosted-runner",
        HostedRunnerDeploymentActions.VerifyStorage => StorageTelemetryActions.MeasureHostedRunnerStorage,
        "measure-hosted-runner-storage" => StorageTelemetryActions.MeasureHostedRunnerStorage,
        _ => null
    };

    private static bool IsTerminalDeployment(string status) => status is
        HostedRunnerDeploymentStatuses.Succeeded or HostedRunnerDeploymentStatuses.Failed
        or HostedRunnerDeploymentStatuses.Skipped;

}

public sealed record InfrastructureCommandSecretsRequest(Guid OperationId, Guid RunnerHostId,
    Guid InfrastructureRunnerId, string ActionKey, IReadOnlyList<Guid> SecretGrantIds);
public sealed record InfrastructureCommandResultRequest(Guid OperationId, Guid RunnerHostId,
    Guid InfrastructureRunnerId, string ActionKey, IReadOnlyList<Guid> SecretGrantIds,
    AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result);
