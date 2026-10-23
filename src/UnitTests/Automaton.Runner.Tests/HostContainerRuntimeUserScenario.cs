using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

internal static class HostContainerRuntimeUserScenario
{
    public static async Task<RuntimeUserOutcome> RunAsync()
    {
        using var machine = MachineContainerTestEnvironment.Create();
        var commands = new List<IReadOnlyList<string>>();
        var result = await CreateService(commands).ExecuteAsync(CreateRequest());
        return new(result.Success, result.Error, IsUserApplied(commands));
    }

    private static HostContainerExecutionService CreateService(
        ICollection<IReadOnlyList<string>> commands)
        => new((arguments, _, _) =>
        {
            commands.Add(arguments);
            return Task.FromResult(new InfrastructureCommandResponse
            {
                Success = !IsMissingCandidate(arguments),
                Error = IsMissingCandidate(arguments) ? $"Error: No such object: {arguments[^1]}" : string.Empty,
                Output = ResponseOutput(arguments)
            });
        }, new MachineWorkloadDiskAdmission(_ => long.MaxValue));

    private static bool IsMissingCandidate(IReadOnlyList<string> arguments)
        => arguments.Contains("container") && arguments.Contains("inspect");

    private static HostContainerExecutionRequest CreateRequest()
        => new()
        {
            Scope = MachineContainerTestEnvironment.CurrentScope(),
            Operation = HostContainerOperationKinds.CreateApplicationContainer,
            RequestedImage = "example/runtime:1",
            CandidateId = Guid.NewGuid(),
            Command = new() { Executable = "serve" },
            ContainerPort = 8080,
            Storage = new(),
            User = "1000:1000",
            Resources = new(
                HostContainerResourceKinds.Source,
                HostContainerResourceKinds.Artifact,
                HostContainerResourceKinds.ApplicationData),
            SharedMemoryMb = 64,
            DockerLogMaxSizeMb = 10,
            DockerLogMaxFiles = 3,
            ContainerLimits = new(1m, 1024, 256), MinimumFreeDiskMb = 256
        };

    private static string ResponseOutput(IReadOnlyList<string> arguments)
    {
        if (arguments.Contains("volume"))
            return MachineContainerTestEnvironment.CurrentVolumeLabels();
        if (arguments.Contains("image"))
            return "example/runtime:1@sha256:" + new string('a', 64);
        return arguments.Contains("create") ? new string('b', 64) : string.Empty;
    }

    private static bool IsUserApplied(IEnumerable<IReadOnlyList<string>> commands)
    {
        var create = commands.SingleOrDefault(command => command.Contains("create"));
        if (create is null)
            return false;
        var userIndex = create.ToList().IndexOf("--user");
        return userIndex >= 0 && create[userIndex + 1] == "1000:1000";
    }

}

internal sealed record RuntimeUserOutcome(bool Success, string Error, bool UserApplied);
