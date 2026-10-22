using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging.Events;
using Microsoft.Extensions.Configuration;

namespace Automaton.Runner.Console.Services;

public sealed class InfrastructureCommandConsumer
{
    private readonly InfrastructureCommandIdentity identity;
    private readonly IInfrastructureActionExecutor executor;
    private readonly InfrastructureCommandQueueConsumer queueConsumer;
    private readonly HostContainerExecutionService? hostContainerExecutor;
    private readonly HostContainerBuildCleanupService? buildCleanup;
    private readonly IHostContainerExecutionCommandClient? hostContainerResults;

    public InfrastructureCommandConsumer(
        InfrastructureCommandIdentity identity,
        IInfrastructureActionExecutor executor,
        IInfrastructureCommandResultClient results)
        : this(identity, executor, results, null, null, null)
    {
    }

    public InfrastructureCommandConsumer(
        InfrastructureCommandIdentity identity,
        IInfrastructureActionExecutor executor,
        IInfrastructureCommandResultClient results,
        HostContainerExecutionService? hostContainerExecutor,
        IHostContainerExecutionCommandClient? hostContainerResults,
        HostContainerBuildCleanupService? buildCleanup)
    {
        this.identity = identity;
        this.executor = executor;
        this.hostContainerExecutor = hostContainerExecutor;
        this.buildCleanup = buildCleanup;
        this.hostContainerResults = hostContainerResults;
        queueConsumer = new InfrastructureCommandQueueConsumer(QueueName, HandleAsync, results, SubmitHostContainerResultAsync);
    }

    public InfrastructureCommandConsumer(InfrastructureCommandIdentity identity, IInfrastructureActionExecutor executor)
        : this(identity, executor, new MissingInfrastructureCommandResultClient())
    {
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public string QueueName => $"automaton.infrastructure.{identity.RunnerHostId:N}";

    public async Task<InfrastructureCommandExecutionResult> HandleAsync(
        ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        var command = JsonSerializer.Deserialize<InfrastructureCommandRequestedEvent>(body.Span, JsonOptions)
            ?? throw new InvalidOperationException("Infrastructure command body is required.");
        if (identity.RunnerHostId == Guid.Empty || identity.RunnerId == Guid.Empty
            || command.CommandId == Guid.Empty || command.OperationId == Guid.Empty
            || command.RunnerHostId != identity.RunnerHostId || command.InfrastructureRunnerId != identity.RunnerId)
            throw new UnauthorizedAccessException("Infrastructure command identity does not match this Runner and Runner Host.");
        if (command.ActionKey == HostContainerExecutionCommandKinds.InfrastructureActionKey)
            return await ExecuteHostContainerAsync(command, cancellationToken);

        if (command.ActionKey is "suspend-hosted-runner" or "resume-hosted-runner" or "measure-hosted-runner-storage")
            return new(null, null, true, "Hosted Runner storage operations are retired.");

        TrustedInfrastructureActions.EnsureAllowed(command.ActionKey);
        ValidateHostValidationCommand(command);
        if (command.SecretGrantIds == null || string.IsNullOrWhiteSpace(command.InputJson)
            || command.CreatedAtUtc == default || command.CreatedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Infrastructure command input, grant IDs, and UTC creation time are required.");
        return await executor.ExecuteAsync(command, cancellationToken);
    }

    private static void ValidateHostValidationCommand(InfrastructureCommandRequestedEvent command)
    {
        if (command.ActionKey == HostValidationCommand.ActionKey
            && (command.CommandId != command.OperationId || command.SecretGrantIds?.Count != 0))
            throw new InvalidDataException("Host validation requires its operation identity and no secret grants.");
    }

    private async Task<InfrastructureCommandExecutionResult> ExecuteHostContainerAsync(
        InfrastructureCommandRequestedEvent command,
        CancellationToken cancellationToken)
    {
        if (hostContainerExecutor is null || hostContainerResults is null || buildCleanup is null)
            throw new InvalidOperationException("Host-container command processing is not configured on this Infrastructure Runner.");
        if (command.CommandId != command.OperationId || command.SecretGrantIds.Count != 0
            || string.IsNullOrWhiteSpace(command.InputJson) || command.CreatedAtUtc == default
            || command.CreatedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("The host-container command identity or secret grants are invalid.");

        var request = JsonSerializer.Deserialize<HostContainerExecutionRequest>(command.InputJson, JsonOptions)
            ?? throw new InvalidDataException("The host-container request is empty.");
        if (request.OperationId != command.OperationId || request.Scope is null
            || request.Scope.RunnerHostId != identity.RunnerHostId)
            throw new UnauthorizedAccessException("The host-container request does not match this Infrastructure Runner Host.");

        var disposition = await hostContainerResults.GetDispositionAsync(command.OperationId, cancellationToken);
        if (disposition is "already-applied" or "obsolete")
        {
            await buildCleanup.CleanupAfterResultAsync(request);
            return new(null, null, true, disposition);
        }
        if (disposition != "execute")
            throw new InvalidDataException("The API returned an unknown host-container command disposition.");

        return new(null, null, false, null,
            await hostContainerExecutor.ExecuteAsync(request, cancellationToken));
    }

    private async Task SubmitHostContainerResultAsync(
        InfrastructureCommandRequestedEvent command,
        InfrastructureCommandExecutionResult result,
        CancellationToken cancellationToken)
    {
        if (result.HostContainerResult is null)
            throw new InvalidOperationException("The host-container command returned no execution result.");
        await hostContainerResults!.SubmitAsync(command, result.HostContainerResult, cancellationToken);
        var request = JsonSerializer.Deserialize<HostContainerExecutionRequest>(command.InputJson, JsonOptions);
        if (request is not null)
            await buildCleanup!.CleanupAfterResultAsync(request);
    }

    public Task<InfrastructureCommandExecutionResult> ConsumeOneAsync(
        IConfiguration configuration, CancellationToken cancellationToken)
        => queueConsumer.ConsumeOneAsync(configuration, cancellationToken);

    public Task ConsumeAsync(IConfiguration configuration, CancellationToken cancellationToken)
        => queueConsumer.ConsumeAsync(configuration, cancellationToken);

    private sealed class MissingInfrastructureCommandResultClient : IInfrastructureCommandResultClient
    {
        public Task SubmitAsync(InfrastructureCommandRequestedEvent command, AppActionExecutionResult execution,
            AppModuleExecutionArtifactResponse result, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Infrastructure command result delivery is required before acknowledgement.");
    }
}
