using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

internal static class HostContainerRuntimeUserScenario
{
    public static async Task<RuntimeUserOutcome> RunAsync()
    {
        var oldPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var oldCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        var oldHostAddress = Environment.GetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable);
        var hostedRunnerId = Guid.NewGuid();
        var commands = new List<IReadOnlyList<string>>();
        try
        {
            SetInfrastructureRunnerEnvironment();
            var result = await CreateService(hostedRunnerId, commands).ExecuteAsync(CreateRequest(hostedRunnerId));
            return new(result.Success, result.Error, IsUserApplied(commands));
        }
        finally
        {
            RestoreEnvironment(oldPurpose, oldCapabilities, oldHostAddress);
        }
    }

    private static HostContainerExecutionService CreateService(
        Guid hostedRunnerId,
        ICollection<IReadOnlyList<string>> commands)
        => new((arguments, _, _) =>
        {
            commands.Add(arguments);
            return Task.FromResult(new InfrastructureCommandResponse
            {
                Success = !IsMissingCandidate(arguments),
                Error = IsMissingCandidate(arguments) ? $"Error: No such object: {arguments[^1]}" : string.Empty,
                Output = ResponseOutput(arguments, hostedRunnerId)
            });
        });

    private static bool IsMissingCandidate(IReadOnlyList<string> arguments)
        => arguments.Contains("container") && arguments.Contains("inspect");

    private static HostContainerExecutionRequest CreateRequest(Guid hostedRunnerId)
        => new()
        {
            Scope = new(hostedRunnerId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
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
            DockerLogMaxFiles = 3
        };

    private static string ResponseOutput(IReadOnlyList<string> arguments, Guid hostedRunnerId)
    {
        if (arguments.Contains("volume"))
            return $"{{\"automaton.managed\":\"true\",\"automaton.resource\":\"hosted-runner-volume\",\"automaton.hosted-runner-id\":\"{hostedRunnerId}\",\"automaton.storage-scope\":\"user\"}}";
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

    private static void SetInfrastructureRunnerEnvironment()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Infrastructure);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.HostContainerExecution);
        Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, "172.30.0.1");
    }

    private static void RestoreEnvironment(string? purpose, string? capabilities, string? hostAddress)
    {
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", purpose);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", capabilities);
        Environment.SetEnvironmentVariable(RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable, hostAddress);
    }
}

internal sealed record RuntimeUserOutcome(bool Success, string Error, bool UserApplied);
