using System.Text.Json;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Events;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class HostContainerCommandReplayTests
{
    [Test]
    public async Task RedeliveryAfterPersistedResultDoesNotRepeatHostExecution()
    {
        var outcome = await HostContainerCommandReplayScenario.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(outcome.First.HostContainerResult?.Success, Is.True);
            Assert.That(outcome.Replay.Terminal, Is.True);
            Assert.That(outcome.Replay.TerminalReason, Is.EqualTo("already-applied"));
            Assert.That(outcome.DockerCommands, Is.EqualTo(2));
        });
    }
}

internal static class HostContainerCommandReplayScenario
{
    public static async Task<ReplayOutcome> RunAsync()
    {
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var priorCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        try
        {
            SetInfrastructureAuthorization();
            var harness = CreateHarness();
            var first = await harness.Consumer.HandleAsync(harness.Body);
            harness.Api.Disposition = "already-applied";
            var replay = await harness.Consumer.HandleAsync(harness.Body);
            return new(first, replay, harness.DockerCommands);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", priorPurpose);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", priorCapabilities);
        }
    }

    private static Harness CreateHarness()
    {
        var identity = new InfrastructureCommandIdentity(Guid.NewGuid(), Guid.NewGuid());
        var request = CreateRequest(identity);
        var command = CreateCommand(identity, request);
        var api = new RecordingHostContainerApi();
        var dockerCommands = 0;
        var host = new HostContainerExecutionService((_, _, _) =>
        {
            dockerCommands++;
            return Task.FromResult(new InfrastructureCommandResponse
            {
                Success = true,
                Output = dockerCommands == 1 ? "pulled" : "example/image@sha256:abc"
            });
        });
        var consumer = new InfrastructureCommandConsumer(identity, new RecordingActionExecutor(),
            new NoOpInfrastructureResultClient(), host, api,
            new HostContainerBuildCleanupService(new InfrastructureCommandService()));
        return new(consumer, api, JsonSerializer.SerializeToUtf8Bytes(command), () => dockerCommands);
    }

    private static HostContainerExecutionRequest CreateRequest(InfrastructureCommandIdentity identity)
        => new()
        {
            OperationId = Guid.NewGuid(),
            Scope = new(Guid.NewGuid(), identity.RunnerHostId, Guid.NewGuid(), Guid.NewGuid()),
            Operation = HostContainerOperationKinds.ResolveImage,
            RequestedImage = "example/image:1"
        };

    private static InfrastructureCommandRequestedEvent CreateCommand(
        InfrastructureCommandIdentity identity,
        HostContainerExecutionRequest request)
        => new(request.OperationId, request.OperationId, identity.RunnerHostId, identity.RunnerId,
            HostContainerExecutionCommandKinds.InfrastructureActionKey, JsonSerializer.Serialize(request), [], DateTime.UtcNow);

    private static void SetInfrastructureAuthorization()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Infrastructure);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.HostContainerExecution);
    }

    internal sealed record ReplayOutcome(
        InfrastructureCommandExecutionResult First,
        InfrastructureCommandExecutionResult Replay,
        int DockerCommands);

    private sealed record Harness(
        InfrastructureCommandConsumer Consumer,
        RecordingHostContainerApi Api,
        ReadOnlyMemory<byte> Body,
        Func<int> ReadDockerCommands)
    {
        public int DockerCommands => ReadDockerCommands();
    }

    private sealed class RecordingActionExecutor : IInfrastructureActionExecutor
    {
        public Task<InfrastructureCommandExecutionResult> ExecuteAsync(
            InfrastructureCommandRequestedEvent command, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Host-container commands do not use trusted actions.");
    }

    private sealed class NoOpInfrastructureResultClient : IInfrastructureCommandResultClient
    {
        public Task SubmitAsync(InfrastructureCommandRequestedEvent command, AppActionExecutionResult execution,
            AppModuleExecutionArtifactResponse result, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RecordingHostContainerApi : IHostContainerExecutionCommandClient
    {
        public string Disposition { get; set; } = "execute";
        public Task<string> GetDispositionAsync(Guid operationId, CancellationToken cancellationToken)
            => Task.FromResult(Disposition);
        public Task SubmitAsync(InfrastructureCommandRequestedEvent command,
            HostContainerExecutionResponse result, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
