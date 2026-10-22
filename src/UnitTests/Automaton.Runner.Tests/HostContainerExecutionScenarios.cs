using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

internal static class HostContainerExecutionScenarios
{
    public static async Task<bool> ProductionTypedDockerPathReceivesDockerPrefixedRunBuildAsync()
    {
        using var machine = MachineContainerTestEnvironment.Create();
        using var testWorkspace = RunnerRuntimeContainerTestWorkspace.Create();
        var calls = new List<IReadOnlyList<string>>();
        var infrastructure = CreateInfrastructureCommandService(calls);
        var service = new HostContainerExecutionService(infrastructure);
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var priorCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Managed);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.ManagedContainerExecution);
            await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = MachineContainerTestEnvironment.CurrentScope(),
                Operation = HostContainerOperationKinds.RunBuild,
                ContainerLimits = new(1m, 1024, 256), MinimumFreeDiskMb = 256, RequestedImage = "example/build:1",
                Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact, HostContainerResourceKinds.ApplicationData),
                SharedMemoryMb = 64, DockerLogMaxSizeMb = 10, DockerLogMaxFiles = 3,
                Command = new() { Executable = "build" }
            });

            return calls.Any(call => call.Count > 1 && call[0] == "docker" && call[1] == "run");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", priorPurpose);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", priorCapabilities);
        }
    }

    private static InfrastructureCommandService CreateInfrastructureCommandService(
        ICollection<IReadOnlyList<string>> calls)
        => new((arguments, _, _) =>
        {
            calls.Add(arguments);
            var output = arguments.Contains("volume")
                ? MachineContainerTestEnvironment.CurrentVolumeLabels()
                : arguments.Contains("image") ? "example/build:1@sha256:abc" : "";
            var missingContainer = arguments.Contains("container") && arguments.Contains("inspect");
            return Task.FromResult(new InfrastructureCommandResponse
            {
                Success = !missingContainer,
                Error = missingContainer ? $"Error: No such object: {arguments[^1]}" : string.Empty,
                Output = output
            });
        });

    private const string Image = "example/build:1@sha256:abc";

    public static async Task<LabeledMachineVolumeBuildOutcome> RunLabeledMachineVolumeBuildAsync()
    {
        using var environment = MachineContainerTestEnvironment.Create();
        var session = new HostContainerExecutionSession();
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
                command.Contains("volume") && command.Contains($"automaton-machine-runner-{environment.InstallationId}-workspace")),
            string.Join(' ', buildCommand),
            scope.AppId,
            scope.DeploymentTargetId,
            scope.DeploymentAttemptId);
    }

    public static async Task<EntrypointVariantsOutcome> RunEntrypointVariantsAsync()
    {
        using var environment = MachineContainerTestEnvironment.Create();
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
        using var environment = MachineContainerTestEnvironment.Create();
        var request = CreateInterruptedBuildRequest();
        var commandState = new InterruptedBuildCommandState(request);
        var service = new HostContainerExecutionService(commandState.ExecuteAsync, new MachineWorkloadDiskAdmission(_ => long.MaxValue));

        var cancellationSurfaced = false;
        try
        {
            await service.ExecuteAsync(request);
        }
        catch (OperationCanceledException)
        {
            cancellationSurfaced = true;
        }

        var retry = await service.ExecuteAsync(request);
        return new(cancellationSurfaced, false, retry.Success, commandState.RunCount, commandState.WaitRequested);
    }

    private sealed class InterruptedBuildCommandState(HostContainerExecutionRequest request)
    {
        private bool containerExists;
        public int RunCount { get; private set; }
        public bool WaitRequested { get; private set; }

        public Task<InfrastructureCommandResponse> ExecuteAsync(
            IReadOnlyList<string> arguments, TimeSpan _, CancellationToken __)
        {
            if (arguments.Contains("volume")) return Task.FromResult(VolumeResponse());
            if (arguments.Contains("image")) return Task.FromResult(new InfrastructureCommandResponse { Success = true, Output = Image });
            if (IsContainerInspection(arguments)) return Task.FromResult(Inspect(arguments));
            if (IsRun(arguments)) return InterruptBuild();
            if (IsWait(arguments)) return CompleteWait();
            return Task.FromResult(new InfrastructureCommandResponse { Success = true });
        }

        private InfrastructureCommandResponse Inspect(IReadOnlyList<string> arguments)
            => new()
            {
                Success = containerExists,
                Error = containerExists ? string.Empty : $"Error: No such object: {arguments[^1]}",
                Output = containerExists ? ExistingBuildInspection(request) : string.Empty
            };

        private Task<InfrastructureCommandResponse> InterruptBuild()
        {
            RunCount++;
            containerExists = true;
            return Task.FromException<InfrastructureCommandResponse>(new OperationCanceledException("host command canceled"));
        }

        private Task<InfrastructureCommandResponse> CompleteWait()
        {
            WaitRequested = true;
            return Task.FromResult(new InfrastructureCommandResponse { Success = true, Output = "0" });
        }

        private static InfrastructureCommandResponse VolumeResponse()
            => new()
            {
                Success = true,
                Output = MachineContainerTestEnvironment.CurrentVolumeLabels()
            };

        private static bool IsContainerInspection(IReadOnlyList<string> args)
            => args.Count > 2 && args[1] == "container" && args[2] == "inspect";
        private static bool IsRun(IReadOnlyList<string> args) => args.Count > 1 && args[1] == "run";
        private static bool IsWait(IReadOnlyList<string> args) => args.Count > 1 && args[1] == "wait";
    }

    private static HostContainerExecutionRequest CreateInterruptedBuildRequest()
        => new()
        {
            OperationId = Guid.NewGuid(),
            Scope = MachineContainerTestEnvironment.CurrentScope(),
            Operation = HostContainerOperationKinds.RunBuild,
            RequestedImage = "example/build:1",
            Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact, HostContainerResourceKinds.ApplicationData),
            SharedMemoryMb = 64, DockerLogMaxSizeMb = 10, DockerLogMaxFiles = 3,
                ContainerLimits = new(1m, 1024, 256), MinimumFreeDiskMb = 256,
            Command = new() { Executable = "build" }
        };

    private static string ExistingBuildInspection(HostContainerExecutionRequest request)
        => "{\"Config\":{\"Labels\":{"
            + "\"automaton.operation-id\":\"" + request.OperationId.ToString("N") + "\","
            + "\"automaton.machine-runner-id\":\"" + request.Scope.MachineRunnerId.ToString("N") + "\","
            + "\"automaton.runner-host-id\":\"" + request.Scope.RunnerHostId.ToString("N") + "\","
            + "\"automaton.app-id\":\"" + request.Scope.AppId.ToString("N") + "\","
            + "\"automaton.deployment-target-id\":\"" + request.Scope.DeploymentTargetId.ToString("N") + "\","
            + "\"automaton.deployment-attempt-id\":\"" + request.Scope.DeploymentAttemptId.ToString("N") + "\","
            + "\"automaton.resolved-image\":\"example/build:1@sha256:abc\"}}}";

    private static async Task<RunBuildOutcome> RunCommandAsync(ApplicationCommandSpec command)
    {
        var session = new HostContainerExecutionSession();
        var result = await session.Service.ExecuteAsync(session.CreateRunBuildRequest(command));
        var runCommand = session.Commands.Single(arguments => arguments.Count > 1 && arguments[1] == "run");
        return new(result.Success, runCommand);
    }

    private static int IndexOfEntrypoint(IReadOnlyList<string> command)
        => command.ToList().IndexOf("--entrypoint");

    private sealed class HostContainerExecutionSession
    {
        public HostContainerExecutionSession()
        {
            Service = new HostContainerExecutionService((arguments, _, _) =>
            {
                Commands.Add(arguments);
                var output = arguments.Contains("volume")
                    ? MachineContainerTestEnvironment.CurrentVolumeLabels()
                    : arguments.Contains("image") ? Image : string.Empty;
                var missingContainer = arguments.Contains("container") && arguments.Contains("inspect");
                return Task.FromResult(new InfrastructureCommandResponse
                {
                    Success = !missingContainer,
                    Error = missingContainer ? $"Error: No such object: {arguments[^1]}" : string.Empty,
                    Output = output
                });
            }, new MachineWorkloadDiskAdmission(_ => long.MaxValue));
        }

        public HostContainerExecutionService Service { get; }
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public HostContainerExecutionRequest CreateRunBuildRequest(
            ApplicationCommandSpec command,
            bool includeBuildMode = false)
            => new()
            {
                Scope = MachineContainerTestEnvironment.CurrentScope(),
                Operation = HostContainerOperationKinds.RunBuild,
                RequestedImage = "example/build:1",
                Resources = new(
                    HostContainerResourceKinds.Source,
                    HostContainerResourceKinds.Artifact,
                    HostContainerResourceKinds.ApplicationData),
                SharedMemoryMb = 64, DockerLogMaxSizeMb = 10, DockerLogMaxFiles = 3,
                ContainerLimits = new(1m, 1024, 256), MinimumFreeDiskMb = 256,
                Command = command,
                Environment = includeBuildMode
                    ? new Dictionary<string, string> { ["BUILD_MODE"] = "test" }
                    : []
            };
    }

    private sealed record RunBuildOutcome(bool Success, IReadOnlyList<string> Command);
}

internal sealed record LabeledMachineVolumeBuildOutcome(
    bool Success,
    bool VolumeNameRequested,
    string BuildCommand,
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

internal sealed record BuildCleanupOutcome(bool CancellationSurfaced, bool CleanupWasRequested,
    bool RetrySucceeded, int RunAttempts, bool WaitRequested);
