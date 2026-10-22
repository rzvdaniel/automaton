using System.Text.Json;
using System.Net;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Events;
using Automaton.Runner.Console.Services;
using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Serialization;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class InfrastructureCommandConsumerTests
{
    private readonly InfrastructureCommandIdentity identity = new(Guid.NewGuid(), Guid.NewGuid());

    [TestCase(HttpStatusCode.OK, InfrastructureCommandResultOutcomes.Accepted, true)]
    [TestCase(HttpStatusCode.OK, InfrastructureCommandResultOutcomes.AlreadyApplied, true)]
    [TestCase(HttpStatusCode.OK, InfrastructureCommandResultOutcomes.TerminalStale, true)]
    [TestCase(HttpStatusCode.Conflict, InfrastructureCommandResultOutcomes.TerminalStale, false)]
    [TestCase(HttpStatusCode.Unauthorized, InfrastructureCommandResultOutcomes.Accepted, false)]
    [TestCase(HttpStatusCode.InternalServerError, InfrastructureCommandResultOutcomes.Accepted, false)]
    [TestCase(HttpStatusCode.OK, "unknown", false)]
    public void ResultCallbackAcknowledgesOnlyAuthoritativeOutcomes(HttpStatusCode statusCode, string outcome, bool expected)
        => Assert.That(InfrastructureCommandApiClient.IsAckableResponse(statusCode,
            new InfrastructureCommandResultResponse(outcome)), Is.EqualTo(expected));

    [Test]
    public void ResultCallbackRejectsMissingOutcome()
        => Assert.That(InfrastructureCommandApiClient.IsAckableResponse(HttpStatusCode.OK, null), Is.False);

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
        Assert.That(result.Execution!.ExecutionId, Is.EqualTo(command.CommandId));
        Assert.That(result.Result!.Content, Is.EqualTo("{\"success\":true}"));
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

    [TestCase("suspend-hosted-runner")]
    [TestCase("resume-hosted-runner")]
    [TestCase("measure-hosted-runner-storage")]
    [TestCase("restart-hosted-runner")]
    [TestCase("delete-hosted-runner")]
    [TestCase("provision-hosted-runner")]
    [TestCase("reprovision-hosted-runner")]
    [TestCase(HostContainerExecutionCommandKinds.InfrastructureActionKey)]
    public async Task RetiredStorageDeliveryIsTerminalWithoutInvokingExecutor(string actionKey)
    {
        var executor = new RecordingExecutor();
        var result = await new InfrastructureCommandConsumer(identity, executor)
            .HandleAsync(JsonSerializer.SerializeToUtf8Bytes(Command() with { ActionKey = actionKey }));
        Assert.That(result.Terminal, Is.True);
        Assert.That(result.Execution, Is.Null);
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

    [TestCase(RunnerPurposes.Infrastructure, "11111111-1111-1111-1111-111111111111", true)]
    [TestCase(RunnerPurposes.Managed, "11111111-1111-1111-1111-111111111111", false)]
    [TestCase("Standard", "11111111-1111-1111-1111-111111111111", false)]
    [TestCase(RunnerPurposes.Infrastructure, "", false)]
    public void InfrastructureServiceStartIsLimitedToValidInfrastructureIdentity(string purpose, string runnerId, bool expected)
        => Assert.That(InfrastructureCommandHostedService.ShouldStart(purpose, runnerId, Guid.NewGuid()), Is.EqualTo(expected));

    private InfrastructureCommandRequestedEvent Command()
    {
        var operationId = Guid.NewGuid();
        return new(operationId, operationId, identity.RunnerHostId, identity.RunnerId,
            HostValidationCommand.ActionKey, "{}", [], DateTime.UtcNow);
    }

    private static async Task<ReadOnlyMemory<byte>> SerializeWithCap(InfrastructureCommandRequestedEvent command)
    {
        var serializer = new JsonUtf8Serializer(Options.Create(new CapOptions()));
        return (await serializer.SerializeAsync(new Message(new Dictionary<string, string?>(), command))).Body;
    }

    private sealed class RecordingExecutor : IInfrastructureActionExecutor
    {
        public List<InfrastructureCommandRequestedEvent> Commands { get; } = [];
        public Task<InfrastructureCommandExecutionResult> ExecuteAsync(
            InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(new InfrastructureCommandExecutionResult(
                new AppActionExecutionResult { ExecutionId = command.CommandId, Status = AppModuleExecutionStatuses.Succeeded },
                new AppModuleExecutionArtifactResponse(true, "{\"success\":true}", null, null), false, null));
        }
    }

}
