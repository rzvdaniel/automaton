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

    public InfrastructureCommandConsumer(
        InfrastructureCommandIdentity identity,
        IInfrastructureActionExecutor executor,
        IInfrastructureCommandResultClient results)
    {
        this.identity = identity;
        this.executor = executor;
        queueConsumer = new InfrastructureCommandQueueConsumer(QueueName, HandleAsync, results);
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
        TrustedInfrastructureActions.EnsureAllowed(command.ActionKey);
        if (command.SecretGrantIds == null || string.IsNullOrWhiteSpace(command.InputJson)
            || command.CreatedAtUtc == default || command.CreatedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Infrastructure command input, grant IDs, and UTC creation time are required.");
        return await executor.ExecuteAsync(command, cancellationToken);
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
