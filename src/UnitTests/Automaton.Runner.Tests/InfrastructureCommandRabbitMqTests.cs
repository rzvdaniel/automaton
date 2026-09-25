using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Messaging.Events;
using Automaton.Runner.Console.Services;
using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using RabbitMQ.Client;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class InfrastructureCommandRabbitMqTests
{
    [Test]
    public async Task LocalRabbitMqLeavesDeliveryUnacknowledgedWhenCallbackFails()
    {
        await using var session = await RabbitMqConsumerTestSession.CreateAsync(RabbitMqTestBehavior.CallbackFails);
        var consuming = session.StartSingleMessageConsumer();
        await session.WaitForConsumerAsync(consuming);
        var commandId = await session.PublishCommandAsync();

        var failure = await session.ReadCallbackFailureAsync(consuming);
        Assert.That(failure, Does.Contain("result delivery is required"));
        Assert.That(session.ExecutedCommandCount, Is.EqualTo(1));

        var redelivery = await session.ReadRedeliveredCommandAsync();
        Assert.That(redelivery.Redelivered, Is.True);
        Assert.That(redelivery.CommandId, Is.EqualTo(commandId));
    }

    [Test]
    public async Task LocalRabbitMqAcknowledgesOnlyAfterCallbackSucceeds()
    {
        await using var session = await RabbitMqConsumerTestSession.CreateAsync(RabbitMqTestBehavior.Succeeds);
        var consuming = session.StartSingleMessageConsumer();
        await session.WaitForConsumerAsync(consuming);
        var commandId = await session.PublishCommandAsync();
        var result = await consuming;

        Assert.That(result.Execution!.ExecutionId, Is.EqualTo(commandId));
        Assert.That(session.CallbackCommandIds.Single(), Is.EqualTo(commandId));
        Assert.That(await session.IsQueueEmptyAsync(), Is.True);
    }

    [Test]
    public async Task LocalRabbitMqLongLivedConsumerConnectsAndConsumes()
    {
        await using var session = await RabbitMqConsumerTestSession.CreateAsync(RabbitMqTestBehavior.Succeeds);
        var running = session.StartLongLivedConsumer();
        await session.WaitForConsumerAsync(running);
        var commandId = await session.PublishCommandAsync();
        await session.WaitForCallbackCountAsync(1);
        Assert.That(session.CallbackCommandIds.Single(), Is.EqualTo(commandId));
        await session.CancelConsumerAsync(running);
    }

    [Test]
    public async Task LocalRabbitMqTerminalCommandIsAckedWithoutExecutionOrConsumerTeardown()
    {
        await using var session = await RabbitMqConsumerTestSession.CreateAsync(RabbitMqTestBehavior.Terminal);
        var running = session.StartLongLivedConsumer();
        await session.WaitForConsumerAsync(running);
        await session.PublishCommandAsync();
        await session.WaitForExecutionCountAsync(1);
        Assert.That(await session.IsQueueEmptyAsync(), Is.True);

        await session.PublishCommandAsync();
        await session.WaitForExecutionCountAsync(2);
        Assert.That(session.CallbackCommandIds, Is.Empty);
        await session.CancelConsumerAsync(running);
    }
}

internal enum RabbitMqTestBehavior
{
    CallbackFails,
    Succeeds,
    Terminal
}

internal sealed class RabbitMqConsumerTestSession : IAsyncDisposable
{
    private readonly IConnection connection;
    private readonly IChannel channel;
    private readonly InfrastructureCommandConsumer consumer;
    private readonly IConfiguration configuration;
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
    private readonly InfrastructureCommandIdentity identity;
    private readonly RecordingExecutor executor;
    private readonly RecordingResultClient callbacks;

    private RabbitMqConsumerTestSession(
        IConnection connection,
        IChannel channel,
        InfrastructureCommandConsumer consumer,
        IConfiguration configuration,
        InfrastructureCommandIdentity identity,
        RecordingExecutor executor,
        RecordingResultClient callbacks)
    {
        this.connection = connection;
        this.channel = channel;
        this.consumer = consumer;
        this.configuration = configuration;
        this.identity = identity;
        this.executor = executor;
        this.callbacks = callbacks;
    }

    public int ExecutedCommandCount => executor.Commands.Count;
    public IReadOnlyList<Guid> CallbackCommandIds => callbacks.Commands.Select(command => command.CommandId).ToArray();

    public static async Task<RabbitMqConsumerTestSession> CreateAsync(RabbitMqTestBehavior behavior)
    {
        var port = GetRabbitMqPort();
        var username = Environment.GetEnvironmentVariable("AUTOMATON_TEST_RABBITMQ_USER") ?? "guest";
        var password = Environment.GetEnvironmentVariable("AUTOMATON_TEST_RABBITMQ_PASSWORD") ?? "guest";
        var factory = new ConnectionFactory
        {
            HostName = "127.0.0.1",
            Port = port,
            UserName = username,
            Password = password,
            AutomaticRecoveryEnabled = false
        };
        using var setupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var connection = await factory.CreateConnectionAsync(setupTimeout.Token);
        var channel = await connection.CreateChannelAsync(cancellationToken: setupTimeout.Token);
        var identity = new InfrastructureCommandIdentity(Guid.NewGuid(), Guid.NewGuid());
        var executor = new RecordingExecutor(behavior);
        var callbacks = new RecordingResultClient();
        var consumer = behavior == RabbitMqTestBehavior.CallbackFails
            ? new InfrastructureCommandConsumer(identity, executor)
            : new InfrastructureCommandConsumer(identity, executor, callbacks);
        var configuration = CreateConfiguration(port, username, password);
        await channel.QueueDeclareAsync(
            consumer.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: setupTimeout.Token);

        return new(connection, channel, consumer, configuration, identity, executor, callbacks);
    }

    public Task<InfrastructureCommandExecutionResult> StartSingleMessageConsumer()
        => consumer.ConsumeOneAsync(configuration, timeout.Token);

    public Task StartLongLivedConsumer()
        => consumer.ConsumeAsync(configuration, timeout.Token);

    public async Task WaitForConsumerAsync(Task consuming)
    {
        while ((await channel.QueueDeclarePassiveAsync(consumer.QueueName, timeout.Token)).ConsumerCount == 0)
        {
            if (consuming.IsCompleted)
                await consuming;
            await Task.Delay(25, timeout.Token);
        }
    }

    public async Task<Guid> PublishCommandAsync()
    {
        var command = CreateCommand();
        var body = await SerializeWithCapAsync(command);
        await channel.BasicPublishAsync(
            "cap.default.router",
            "infrastructure-command.requested",
            mandatory: true,
            new BasicProperties { Persistent = true },
            body,
            timeout.Token);
        return command.CommandId;
    }

    public async Task<string> ReadCallbackFailureAsync(Task<InfrastructureCommandExecutionResult> consuming)
    {
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await consuming);
        return exception!.Message;
    }

    public async Task<(bool Redelivered, Guid CommandId)> ReadRedeliveredCommandAsync()
    {
        BasicGetResult? delivery;
        while ((delivery = await channel.BasicGetAsync(consumer.QueueName, autoAck: false, timeout.Token)) is null)
            await Task.Delay(25, timeout.Token);
        var command = JsonSerializer.Deserialize<InfrastructureCommandRequestedEvent>(delivery.Body.Span)!;
        return (delivery.Redelivered, command.CommandId);
    }

    public async Task<bool> IsQueueEmptyAsync()
        => await channel.BasicGetAsync(consumer.QueueName, autoAck: true, timeout.Token) is null;

    public async Task WaitForCallbackCountAsync(int count)
    {
        while (callbacks.Commands.Count < count)
            await Task.Delay(25, timeout.Token);
    }

    public async Task WaitForExecutionCountAsync(int count)
    {
        while (executor.Commands.Count < count)
            await Task.Delay(25, timeout.Token);
    }

    public async Task CancelConsumerAsync(Task consuming)
    {
        timeout.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await consuming);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await channel.QueueDeleteAsync(consumer.QueueName, ifUnused: false, ifEmpty: false);
        }
        finally
        {
            timeout.Cancel();
            timeout.Dispose();
            await channel.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private InfrastructureCommandRequestedEvent CreateCommand()
    {
        var operationId = Guid.NewGuid();
        return new(
            operationId,
            operationId,
            identity.RunnerHostId,
            identity.RunnerId,
            "restart-hosted-runner",
            "{\"containerName\":\"test-runner\"}",
            [Guid.NewGuid()],
            DateTime.UtcNow);
    }

    private static int GetRabbitMqPort()
    {
        var portText = Environment.GetEnvironmentVariable("AUTOMATON_TEST_RABBITMQ_PORT");
        if (!int.TryParse(portText, out var port))
            Assert.Ignore("Set AUTOMATON_TEST_RABBITMQ_PORT for an isolated local RabbitMQ instance.");
        return port;
    }

    private static IConfiguration CreateConfiguration(int port, string username, string password)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "127.0.0.1",
            ["RabbitMq:Port"] = port.ToString(),
            ["RabbitMq:Username"] = username,
            ["RabbitMq:Password"] = password
        }).Build();

    private static async Task<ReadOnlyMemory<byte>> SerializeWithCapAsync(InfrastructureCommandRequestedEvent command)
    {
        var serializer = new JsonUtf8Serializer(Options.Create(new CapOptions()));
        var message = new Message(new Dictionary<string, string?>(), command);
        return (await serializer.SerializeAsync(message)).Body;
    }

    private sealed class RecordingExecutor(RabbitMqTestBehavior behavior) : IInfrastructureActionExecutor
    {
        public List<InfrastructureCommandRequestedEvent> Commands { get; } = [];

        public Task<InfrastructureCommandExecutionResult> ExecuteAsync(
            InfrastructureCommandRequestedEvent command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(behavior == RabbitMqTestBehavior.Terminal
                ? new InfrastructureCommandExecutionResult(null, null, true, "AmbiguousDispatch")
                : new InfrastructureCommandExecutionResult(
                    new AppActionExecutionResult
                    {
                        ExecutionId = command.CommandId,
                        Status = AppModuleExecutionStatuses.Succeeded
                    },
                    new AppModuleExecutionArtifactResponse(true, "{\"success\":true}", null, null),
                    false,
                    null));
        }
    }

    private sealed class RecordingResultClient : IInfrastructureCommandResultClient
    {
        public List<InfrastructureCommandRequestedEvent> Commands { get; } = [];

        public Task SubmitAsync(
            InfrastructureCommandRequestedEvent command,
            AppActionExecutionResult execution,
            AppModuleExecutionArtifactResponse result,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.CompletedTask;
        }
    }
}
