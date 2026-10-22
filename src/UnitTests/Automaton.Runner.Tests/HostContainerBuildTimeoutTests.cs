using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class HostContainerBuildTimeoutTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task TimeoutStopsBuildLocallyAndRetainsContainerForResultRecovery(bool recovering)
    {
        using var machine = MachineContainerTestEnvironment.Create();
        var request = CreateRequest();
        var commands = new List<IReadOnlyList<string>>();
        var stopToken = new CancellationToken(true);
        var executor = new HostContainerBuildExecutor((arguments, _, cancellationToken) =>
        {
            commands.Add(arguments);
            if (arguments[1] == "kill") stopToken = cancellationToken;
            return Task.FromResult(Respond(arguments, request, recovering));
        });

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("build_timeout"));
            Assert.That(commands.Count(command => command[1] == "kill"), Is.EqualTo(1));
            Assert.That(commands.Single(command => command[1] == "kill")[2],
                Is.EqualTo(HostContainerBuildContainerName.For(request.Scope)));
            Assert.That(stopToken.CanBeCanceled, Is.False);
            Assert.That(commands.Any(command => command[1] == "rm"), Is.False);
            Assert.That(commands.Any(command => command[1] == "run"), Is.EqualTo(!recovering));
        });
    }

    private static HostContainerExecutionRequest CreateRequest() => new()
    {
        OperationId = Guid.NewGuid(),
        Scope = MachineContainerTestEnvironment.CurrentScope(),
        Operation = HostContainerOperationKinds.RunBuild,
        RequestedImage = "example/build:1",
        Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact,
            HostContainerResourceKinds.ApplicationData),
        SharedMemoryMb = 64, DockerLogMaxSizeMb = 10, DockerLogMaxFiles = 3,
        ContainerLimits = new(1m, 1024, 256), MinimumFreeDiskMb = 256,
        TimeoutSeconds = 30,
        Command = new() { Executable = "build" }
    };

    private static InfrastructureCommandResponse Respond(
        IReadOnlyList<string> arguments,
        HostContainerExecutionRequest request,
        bool recovering)
    {
        if (arguments[1] == "volume")
            return Success(MachineContainerTestEnvironment.CurrentVolumeLabels());
        if (arguments[1] == "container")
            return recovering ? ExistingContainer(request) : new()
            {
                Error = $"Error: No such object: {arguments[^1]}"
            };
        if (arguments[1] == "image") return Success("example/build:1@sha256:abc");
        if (arguments[1] is "run" or "wait")
            return new() { ExitStatus = 124, Error = "Typed Docker operation timed out." };
        return Success(string.Empty);
    }

    private static InfrastructureCommandResponse ExistingContainer(HostContainerExecutionRequest request)
        => Success(JsonSerializer.Serialize(new
        {
            Config = new
            {
                Labels = new Dictionary<string, string>
                {
                    ["automaton.operation-id"] = request.OperationId.ToString("N"),
                    ["automaton.machine-runner-id"] = request.Scope.MachineRunnerId.ToString("N"),
                    ["automaton.runner-host-id"] = request.Scope.RunnerHostId.ToString("N"),
                    ["automaton.app-id"] = request.Scope.AppId.ToString("N"),
                    ["automaton.deployment-target-id"] = request.Scope.DeploymentTargetId.ToString("N"),
                    ["automaton.deployment-attempt-id"] = request.Scope.DeploymentAttemptId.ToString("N"),
                    ["automaton.resolved-image"] = "example/build:1@sha256:abc"
                }
            }
        }));

    private static InfrastructureCommandResponse Success(string output)
        => new() { Success = true, Output = output };
}
