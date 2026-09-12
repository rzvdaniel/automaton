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
public sealed class InfrastructureCommandConsumerTests
{
    private readonly InfrastructureCommandIdentity identity = new(Guid.NewGuid(), Guid.NewGuid());

    [Test]
    public async Task CapSerializedCommandPreservesFieldsAndInvokesExecutorOnce()
    {
        var executor = new RecordingExecutor();
        var consumer = new InfrastructureCommandConsumer(identity, executor);
        var command = Command();
        var body = await SerializeWithCap(command);

        var result = await consumer.HandleAsync(body);

        Assert.That(executor.Commands.Count, Is.EqualTo(1));
        Assert.That(JsonSerializer.Serialize(executor.Commands.Single()), Is.EqualTo(JsonSerializer.Serialize(command)));
        Assert.That(result.Execution.ExecutionId, Is.EqualTo(command.CommandId));
        Assert.That(result.Result.Content, Is.EqualTo("{\"success\":true}"));
    }

    [TestCase("host")]
    [TestCase("runner")]
    [TestCase("command")]
    [TestCase("operation")]
    public void RejectsWrongOrMissingIdentityWithoutExecution(string field)
    {
        var executor = new RecordingExecutor();
        var command = field switch
        {
            "host" => Command() with { RunnerHostId = Guid.NewGuid() },
            "runner" => Command() with { InfrastructureRunnerId = Guid.NewGuid() },
            "command" => Command() with { CommandId = Guid.Empty },
            _ => Command() with { OperationId = Guid.Empty }
        };

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => new InfrastructureCommandConsumer(identity, executor)
            .HandleAsync(JsonSerializer.SerializeToUtf8Bytes(command)));
        Assert.That(executor.Commands, Is.Empty);
    }

    [TestCase("untrusted")]
    [TestCase("../restart-hosted-runner")]
    public void RejectsUnsupportedActionWithoutExecution(string actionKey)
    {
        var executor = new RecordingExecutor();
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => new InfrastructureCommandConsumer(identity, executor)
            .HandleAsync(JsonSerializer.SerializeToUtf8Bytes(Command() with { ActionKey = actionKey })));
        Assert.That(executor.Commands, Is.Empty);
    }

    [Test]
    public void RejectsMalformedMessageWithoutExecution()
    {
        var executor = new RecordingExecutor();
        Assert.ThrowsAsync<JsonException>(() => new InfrastructureCommandConsumer(identity, executor).HandleAsync("{"u8.ToArray()));
        Assert.That(executor.Commands, Is.Empty);
    }

    [Test]
    public void RunnerAssemblyHasNoCapOrDatabaseDependency()
    {
        var references = typeof(InfrastructureCommandConsumer).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.That(references, Does.Contain("RabbitMQ.Client"));
        Assert.That(references.Any(name => name!.Contains("CAP") || name.Contains("MySql") || name.Contains("EntityFramework")), Is.False);
        Assert.That(typeof(InfrastructureCommandConsumer).GetConstructors().Any(constructor =>
            constructor.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[]
            { typeof(InfrastructureCommandIdentity), typeof(IInfrastructureActionExecutor), typeof(IInfrastructureCommandResultClient) })), Is.True);
    }

    [Test]
    public async Task LocalRabbitMqLeavesDeliveryUnacknowledgedWhenCallbackFails()
    {
        var portText = Environment.GetEnvironmentVariable("AUTOMATON_TEST_RABBITMQ_PORT");
        if (!int.TryParse(portText, out var port))
            Assert.Ignore("Set AUTOMATON_TEST_RABBITMQ_PORT for an isolated local RabbitMQ instance with guest credentials.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var factory = new ConnectionFactory
        {
            HostName = "127.0.0.1", Port = port,
            UserName = Environment.GetEnvironmentVariable("AUTOMATON_TEST_RABBITMQ_USER") ?? "guest",
            Password = Environment.GetEnvironmentVariable("AUTOMATON_TEST_RABBITMQ_PASSWORD") ?? "guest",
            AutomaticRecoveryEnabled = false
        };
        await using var connection = await factory.CreateConnectionAsync(timeout.Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
        var executor = new RecordingExecutor();
        var consumer = new InfrastructureCommandConsumer(identity, executor);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "127.0.0.1", ["RabbitMq:Port"] = port.ToString(),
            ["RabbitMq:Username"] = Environment.GetEnvironmentVariable("AUTOMATON_TEST_RABBITMQ_USER") ?? "guest",
            ["RabbitMq:Password"] = Environment.GetEnvironmentVariable("AUTOMATON_TEST_RABBITMQ_PASSWORD") ?? "guest"
        }).Build();
        await channel.QueueDeclareAsync(consumer.QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: timeout.Token);
        try
        {
            var consumed = consumer.ConsumeOneAsync(configuration, timeout.Token);
            while ((await channel.QueueDeclarePassiveAsync(consumer.QueueName, timeout.Token)).ConsumerCount == 0)
            {
                if (consumed.IsCompleted) await consumed;
                await Task.Delay(25, timeout.Token);
            }
            var command = Command();
            await channel.BasicPublishAsync("cap.default.router", "infrastructure-command.requested", mandatory: true,
                new BasicProperties { Persistent = true }, await SerializeWithCap(command), timeout.Token);
            var callbackFailure = Assert.ThrowsAsync<InvalidOperationException>(async () => await consumed);
            Assert.That(callbackFailure!.Message, Does.Contain("result delivery is required"));
            Assert.That(executor.Commands.Count, Is.EqualTo(1));

            BasicGetResult? redelivered;
            while ((redelivered = await channel.BasicGetAsync(consumer.QueueName, autoAck: false, timeout.Token)) == null)
                await Task.Delay(25, timeout.Token);
            Assert.That(redelivered.Redelivered, Is.True);
            Assert.That(JsonSerializer.Deserialize<InfrastructureCommandRequestedEvent>(redelivered.Body.Span)!.CommandId, Is.EqualTo(command.CommandId));
        }
        finally
        {
            await channel.QueueDeleteAsync(consumer.QueueName, ifUnused: false, ifEmpty: false);
        }
    }

    [Test]
    public async Task LocalRabbitMqAcknowledgesOnlyAfterCallbackSucceeds()
    {
        var portText = Environment.GetEnvironmentVariable("AUTOMATON_TEST_RABBITMQ_PORT");
        if (!int.TryParse(portText, out var port))
            Assert.Ignore("Set AUTOMATON_TEST_RABBITMQ_PORT for an isolated local RabbitMQ instance.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var username = Environment.GetEnvironmentVariable("AUTOMATON_TEST_RABBITMQ_USER") ?? "guest";
        var password = Environment.GetEnvironmentVariable("AUTOMATON_TEST_RABBITMQ_PASSWORD") ?? "guest";
        var factory = new ConnectionFactory { HostName = "127.0.0.1", Port = port, UserName = username, Password = password, AutomaticRecoveryEnabled = false };
        await using var connection = await factory.CreateConnectionAsync(timeout.Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
        var executor = new RecordingExecutor();
        var callbacks = new RecordingResultClient();
        var consumer = new InfrastructureCommandConsumer(identity, executor, callbacks);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "127.0.0.1", ["RabbitMq:Port"] = port.ToString(),
            ["RabbitMq:Username"] = username, ["RabbitMq:Password"] = password
        }).Build();
        await channel.QueueDeclareAsync(consumer.QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: timeout.Token);
        try
        {
            var consumed = consumer.ConsumeOneAsync(configuration, timeout.Token);
            while ((await channel.QueueDeclarePassiveAsync(consumer.QueueName, timeout.Token)).ConsumerCount == 0)
                await Task.Delay(25, timeout.Token);
            var command = Command();
            await channel.BasicPublishAsync("cap.default.router", "infrastructure-command.requested", mandatory: true,
                new BasicProperties { Persistent = true }, await SerializeWithCap(command), timeout.Token);
            var result = await consumed;
            Assert.That(result.Execution.ExecutionId, Is.EqualTo(command.CommandId));
            Assert.That(callbacks.Commands.Single().CommandId, Is.EqualTo(command.CommandId));
            Assert.That(await channel.BasicGetAsync(consumer.QueueName, autoAck: true, timeout.Token), Is.Null);
        }
        finally
        {
            await channel.QueueDeleteAsync(consumer.QueueName, ifUnused: false, ifEmpty: false);
        }
    }

    private InfrastructureCommandRequestedEvent Command()
    {
        var operationId = Guid.NewGuid();
        return new(operationId, operationId, identity.RunnerHostId, identity.RunnerId,
            "restart-hosted-runner", "{\"containerName\":\"test-runner\"}", [Guid.NewGuid()], DateTime.UtcNow);
    }

    private static async Task<ReadOnlyMemory<byte>> SerializeWithCap(InfrastructureCommandRequestedEvent command)
    {
        var serializer = new JsonUtf8Serializer(Options.Create(new CapOptions()));
        return (await serializer.SerializeAsync(new Message(new Dictionary<string, string?>(), command))).Body;
    }

    private sealed class RecordingExecutor : IInfrastructureActionExecutor
    {
        public List<InfrastructureCommandRequestedEvent> Commands { get; } = [];
        public Task<(AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result)> ExecuteAsync(
            InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult((new AppActionExecutionResult { ExecutionId = command.CommandId, Status = AppModuleExecutionStatuses.Succeeded },
                new AppModuleExecutionArtifactResponse(true, "{\"success\":true}", null, null)));
        }
    }

    private sealed class RecordingResultClient : IInfrastructureCommandResultClient
    {
        public List<InfrastructureCommandRequestedEvent> Commands { get; } = [];
        public Task SubmitAsync(InfrastructureCommandRequestedEvent command, AppActionExecutionResult execution,
            AppModuleExecutionArtifactResponse result, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.CompletedTask;
        }
    }
}
