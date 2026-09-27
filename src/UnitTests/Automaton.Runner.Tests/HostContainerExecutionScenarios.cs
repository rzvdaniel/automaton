using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

internal static class HostContainerExecutionScenarios
{
    private const string Image = "example/build:1@sha256:abc";

    public static async Task<LabeledHostedVolumeBuildOutcome> RunLabeledHostedVolumeBuildAsync()
    {
        using var environment = InfrastructureRunnerEnvironment.Create();
        var hostedRunnerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var session = new HostContainerExecutionSession(hostedRunnerId);
        var request = session.CreateRunBuildRequest(new()
        {
            Executable = "build-tool",
            Arguments = ["build"]
        }, includeBuildMode: true);
        var result = await session.Service.ExecuteAsync(request);
        var buildCommand = session.Commands.Single(command => string.Join(' ', command).Contains("runtime-kind=build"));
        var scope = request.Scope;

        return new(
            result.Success,
            session.Commands.Any(command =>
                command.Contains("volume") && command.Contains("automaton-hosted-runner-00000000000000000000000000000001-user")),
            string.Join(' ', buildCommand),
            hostedRunnerId,
            scope.AppId,
            scope.DeploymentTargetId,
            scope.DeploymentAttemptId);
    }

    public static async Task<EntrypointVariantsOutcome> RunEntrypointVariantsAsync()
    {
        using var environment = InfrastructureRunnerEnvironment.Create();
        var path = await RunCommandAsync(new()
        {
            Executable = "dotnet",
            Arguments = ["publish", "-c", "Release"]
        });
        var sourceRelative = await RunCommandAsync(new() { Executable = "scripts/build.sh" });
        var shell = await RunCommandAsync(new()
        {
            Executable = "echo build",
            UseShell = true,
            Arguments = ["-x"]
        });

        return new(
            path.Success,
            path.Command,
            IndexOfEntrypoint(path.Command),
            sourceRelative.Success,
            sourceRelative.Command,
            IndexOfEntrypoint(sourceRelative.Command),
            shell.Success,
            shell.Command,
            IndexOfEntrypoint(shell.Command));
    }

    public static async Task<BuildCleanupOutcome> RunBuildWithHostCommandExceptionAsync()
    {
        using var environment = InfrastructureRunnerEnvironment.Create();
        var hostedRunnerId = Guid.NewGuid();
        var cleanupCalls = new List<IReadOnlyList<string>>();
        var service = new HostContainerExecutionService((arguments, _, _) =>
        {
            if (arguments.Contains("volume"))
            {
                return Task.FromResult(new InfrastructureCommandResponse
                {
                    Success = true,
                    Output = $"{{\"automaton.managed\":\"true\",\"automaton.resource\":\"hosted-runner-volume\",\"automaton.hosted-runner-id\":\"{hostedRunnerId}\",\"automaton.storage-scope\":\"user\"}}"
                });
            }

            if (arguments.Contains("image"))
                return Task.FromResult(new InfrastructureCommandResponse { Success = true, Output = Image });

            if (arguments.Count > 1 && arguments[1] == "run")
                return Task.FromException<InfrastructureCommandResponse>(new OperationCanceledException("host command canceled"));

            cleanupCalls.Add(arguments);
            return Task.FromResult(new InfrastructureCommandResponse { Success = true });
        });
        var request = new HostContainerExecutionRequest
        {
            Scope = new(hostedRunnerId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Operation = HostContainerOperationKinds.RunBuild,
            RequestedImage = "example/build:1",
            Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact, HostContainerResourceKinds.ApplicationData),
            SharedMemoryMb = 64,
            DockerLogMaxSizeMb = 10,
            DockerLogMaxFiles = 3,
            Command = new() { Executable = "build" }
        };

        var cancellationSurfaced = false;
        try
        {
            await service.ExecuteAsync(request);
        }
        catch (OperationCanceledException)
        {
            cancellationSurfaced = true;
        }

        var cleanupWasRequested = cleanupCalls.Any(call =>
            call.Count > 3 && call[0] == "docker" && call[1] == "rm" && call[2] == "-f");
        return new(cancellationSurfaced, cleanupWasRequested);
    }

    private static async Task<RunBuildOutcome> RunCommandAsync(ApplicationCommandSpec command)
    {
        var session = new HostContainerExecutionSession(Guid.NewGuid());
        var result = await session.Service.ExecuteAsync(session.CreateRunBuildRequest(command));
        var runCommand = session.Commands.Single(arguments => arguments.Count > 1 && arguments[1] == "run");
        return new(result.Success, runCommand);
    }

    private static int IndexOfEntrypoint(IReadOnlyList<string> command)
        => command.ToList().IndexOf("--entrypoint");

    private sealed class HostContainerExecutionSession
    {
        public HostContainerExecutionSession(Guid hostedRunnerId)
        {
            HostedRunnerId = hostedRunnerId;
            Service = new HostContainerExecutionService((arguments, _, _) =>
            {
                Commands.Add(arguments);
                var output = arguments.Contains("volume")
                    ? $"{{\"automaton.managed\":\"true\",\"automaton.resource\":\"hosted-runner-volume\",\"automaton.hosted-runner-id\":\"{HostedRunnerId}\",\"automaton.storage-scope\":\"user\"}}"
                    : arguments.Contains("inspect") ? Image : string.Empty;
                return Task.FromResult(new InfrastructureCommandResponse { Success = true, Output = output });
            });
        }

        public Guid HostedRunnerId { get; }
        public HostContainerExecutionService Service { get; }
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public HostContainerExecutionRequest CreateRunBuildRequest(
            ApplicationCommandSpec command,
            bool includeBuildMode = false)
            => new()
            {
                Scope = new(HostedRunnerId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                Operation = HostContainerOperationKinds.RunBuild,
                RequestedImage = "example/build:1",
                Resources = new(
                    HostContainerResourceKinds.Source,
                    HostContainerResourceKinds.Artifact,
                    HostContainerResourceKinds.ApplicationData),
                SharedMemoryMb = 64, DockerLogMaxSizeMb = 10, DockerLogMaxFiles = 3,
                Command = command,
                Environment = includeBuildMode
                    ? new Dictionary<string, string> { ["BUILD_MODE"] = "test" }
                    : []
            };
    }

    private sealed class InfrastructureRunnerEnvironment : IDisposable
    {
        private readonly string? previousPurpose;
        private readonly string? previousCapabilities;

        private InfrastructureRunnerEnvironment()
        {
            previousPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
            previousCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Infrastructure);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.HostContainerExecution);
        }

        public static InfrastructureRunnerEnvironment Create() => new();

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", previousPurpose);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", previousCapabilities);
        }
    }

    private sealed record RunBuildOutcome(bool Success, IReadOnlyList<string> Command);
}

internal sealed record LabeledHostedVolumeBuildOutcome(
    bool Success,
    bool VolumeNameRequested,
    string BuildCommand,
    Guid HostedRunnerId,
    Guid AppId,
    Guid DeploymentTargetId,
    Guid DeploymentAttemptId);

internal sealed record EntrypointVariantsOutcome(
    bool PathSucceeded,
    IReadOnlyList<string> PathCommand,
    int PathEntryPoint,
    bool SourceRelativeSucceeded,
    IReadOnlyList<string> SourceRelativeCommand,
    int SourceRelativeEntryPoint,
    bool ShellSucceeded,
    IReadOnlyList<string> ShellCommand,
    int ShellEntryPoint);

internal sealed record BuildCleanupOutcome(bool CancellationSurfaced, bool CleanupWasRequested);
