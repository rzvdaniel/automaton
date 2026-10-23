using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class MachineContainerResourceLimitsTests
{
    [Test]
    public async Task BuildContainerReceivesServerDerivedCpuMemoryAndPidLimits()
    {
        using var environment = MachineContainerTestEnvironment.Create();
        var run = await MachineBuildContainerLimitScenario.ExecuteAsync(environment);

        Assert.Multiple(() =>
        {
            Assert.That(run.Success, Is.True);
            AssertContainerLimits(run.Arguments);
            Assert.That(run.Arguments, Does.Not.Contain("--cgroup-parent"));
        });
    }

    [Test]
    public async Task AppContainerReceivesServerDerivedCpuMemoryAndPidLimits()
    {
        using var environment = MachineContainerTestEnvironment.Create();
        var create = await MachineAppContainerLimitScenario.ExecuteAsync(environment);

        Assert.Multiple(() =>
        {
            Assert.That(create.Success, Is.True);
            AssertContainerLimits(create.Arguments);
            Assert.That(create.Arguments, Does.Not.Contain("--cgroup-parent"));
        });
    }

    [Test]
    public async Task BuildContainerIsRefusedBelowTheConfiguredFreeDiskReserve()
    {
        using var environment = MachineContainerTestEnvironment.Create();
        var result = await MachineBuildContainerLowDiskScenario.ExecuteAsync(environment);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("machine_disk_reserve_unavailable"));
            Assert.That(result.Error, Does.Contain("configured minimum is 1024 MB"));
            Assert.That(result.CommandCount, Is.Zero);
        });
    }

    private static void AssertContainerLimits(IReadOnlyList<string> arguments)
    {
        var values = arguments.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(values[Array.IndexOf(values, "--cpus") + 1], Is.EqualTo("1.0"));
            Assert.That(values[Array.IndexOf(values, "--memory") + 1], Is.EqualTo("1024m"));
            Assert.That(values[Array.IndexOf(values, "--pids-limit") + 1], Is.EqualTo("256"));
        });
    }
}

internal sealed record MachineContainerRunResult(bool Success, IReadOnlyList<string> Arguments,
    int CommandCount = 0, string ErrorCode = "", string Error = "");

internal static class MachineBuildContainerLimitScenario
{
    public static async Task<MachineContainerRunResult> ExecuteAsync(MachineContainerTestEnvironment environment)
    {
        var docker = new MachineContainerDockerExecutor(environment.InstallationId);
        var service = new HostContainerExecutionService(docker.ExecuteAsync);
        var request = new HostContainerExecutionRequest
        {
            OperationId = Guid.NewGuid(),
            Scope = new(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), environment.RunnerId),
            Operation = HostContainerOperationKinds.RunBuild,
            RequestedImage = "example/app:1",
            TimeoutSeconds = 30,
            Command = new() { Executable = "npm", Arguments = ["run", "build"] },
            Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact,
                HostContainerResourceKinds.ApplicationData),
            ContainerLimits = new(1.0m, 1024, 256),
            SharedMemoryMb = 64,
            DockerLogMaxSizeMb = 20,
            DockerLogMaxFiles = 5,
            MinimumFreeDiskMb = 1024
        };
        var result = await service.ExecuteAsync(request);
        return new(result.Success, docker.FindCommand("run"));
    }
}

internal static class MachineBuildContainerLowDiskScenario
{
    public static async Task<MachineContainerRunResult> ExecuteAsync(MachineContainerTestEnvironment environment)
    {
        var docker = new MachineContainerDockerExecutor(environment.InstallationId);
        var service = new HostContainerExecutionService(
            docker.ExecuteAsync, new MachineWorkloadDiskAdmission(_ => 0));
        var result = await service.ExecuteAsync(new HostContainerExecutionRequest
        {
            OperationId = Guid.NewGuid(),
            Scope = new(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), environment.RunnerId),
            Operation = HostContainerOperationKinds.RunBuild,
            RequestedImage = "example/build:1",
            TimeoutSeconds = 30,
            Command = new() { Executable = "npm", Arguments = ["run", "build"] },
            Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact,
                HostContainerResourceKinds.ApplicationData),
            ContainerLimits = new(1.0m, 1024, 256),
            SharedMemoryMb = 64,
            DockerLogMaxSizeMb = 20,
            DockerLogMaxFiles = 5,
            MinimumFreeDiskMb = 1024
        });
        return new(result.Success, [], docker.Commands.Count, result.ErrorCode, result.Error);
    }
}

internal static class MachineAppContainerLimitScenario
{
    public static async Task<MachineContainerRunResult> ExecuteAsync(MachineContainerTestEnvironment environment)
    {
        var docker = new MachineContainerDockerExecutor(environment.InstallationId);
        var service = new HostContainerExecutionService(docker.ExecuteAsync);
        var request = new HostContainerExecutionRequest
        {
            OperationId = Guid.NewGuid(),
            Scope = new(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), environment.RunnerId),
            Operation = HostContainerOperationKinds.CreateApplicationContainer,
            RequestedImage = "example/app:1",
            CandidateId = Guid.NewGuid(),
            ContainerPort = 8080,
            Command = new() { Executable = "node", Arguments = ["server.js"] },
            Storage = new ApplicationStorageSpec(),
            Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact,
                HostContainerResourceKinds.ApplicationData),
            ContainerLimits = new(1.0m, 1024, 256),
            SharedMemoryMb = 64,
            DockerLogMaxSizeMb = 20,
            DockerLogMaxFiles = 5,
            MinimumFreeDiskMb = 1024
        };
        var result = await service.ExecuteAsync(request);
        return new(result.Success, docker.FindCommand("create"));
    }
}

internal sealed class MachineContainerTestEnvironment : IDisposable
{
    private static readonly string[] Names =
    [
        "AUTOMATON_RUNNER_PURPOSE", "AUTOMATON_RUNNER_APPLICATION_PROFILE", "AUTOMATON_RUNNER_CAPABILITIES",
        "AUTOMATON_RUNNER_ID", "AUTOMATON_RUNNER_INSTALLATION_ID", "AUTOMATON_RUNTIME_HOST_ADDRESS",
        "AUTOMATON_LOCAL_DOCKER_TEST"
    ];
    private readonly string?[] previousValues;

    private MachineContainerTestEnvironment(Guid runnerId, string installationId, string?[] previousValues)
    {
        RunnerId = runnerId;
        InstallationId = installationId;
        this.previousValues = previousValues;
    }

    public Guid RunnerId { get; }
    public string InstallationId { get; }

    public static MachineContainerTestEnvironment Create()
    {
        var previous = Names.Select(Environment.GetEnvironmentVariable).ToArray();
        var runnerId = Guid.NewGuid();
        var installationId = "sh_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(Names[0], RunnerPurposes.Managed);
        Environment.SetEnvironmentVariable(Names[1], RunnerApplicationProfiles.MachineManagement);
        Environment.SetEnvironmentVariable(Names[2], RunnerCapabilities.ManagedContainerExecution);
        Environment.SetEnvironmentVariable(Names[3], runnerId.ToString());
        Environment.SetEnvironmentVariable(Names[4], installationId);
        Environment.SetEnvironmentVariable(Names[5], "127.0.0.1");
        Environment.SetEnvironmentVariable(Names[6], "true");
        return new(runnerId, installationId, previous);
    }

    public static HostContainerScope CurrentScope(Guid? appId = null, Guid? targetId = null, Guid? attemptId = null)
        => new(Guid.Empty, Guid.Empty, appId ?? Guid.NewGuid(), targetId ?? Guid.NewGuid(), attemptId ?? Guid.NewGuid(),
            Guid.Parse(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_ID")!));

    public static string CurrentVolumeLabels()
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["automaton.managed"] = "true", ["automaton.resource"] = "machine-workspace-volume",
            ["automaton.storage-scope"] = "workspace",
            ["automaton.installation-id"] = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID")!
        });

    public void Dispose()
    {
        for (var index = 0; index < Names.Length; index++)
            Environment.SetEnvironmentVariable(Names[index], previousValues[index]);
    }
}

internal sealed class MachineContainerDockerExecutor(string installationId)
{
    private readonly List<IReadOnlyList<string>> commands = [];
    public IReadOnlyList<IReadOnlyList<string>> Commands => commands;
    private readonly string volumeLabels = $$"""{"automaton.managed":"true","automaton.resource":"machine-workspace-volume","automaton.installation-id":"{{installationId}}","automaton.storage-scope":"workspace"}""";

    public Task<InfrastructureCommandResponse> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken token)
    {
        commands.Add(arguments.ToArray());
        var output = arguments[1] switch
        {
            "volume" => volumeLabels,
            "image" => "example/app:1@sha256:" + new string('a', 64),
            "create" => new string('b', 64),
            _ => "pulled"
        };
        var missingContainer = arguments.Count > 2 && arguments[1] == "container" && arguments[2] == "inspect";
        return Task.FromResult(new InfrastructureCommandResponse
        {
            Success = !missingContainer,
            Error = missingContainer ? $"Error: No such object: {arguments[^1]}" : string.Empty,
            Output = output
        });
    }

    public IReadOnlyList<string> FindCommand(string operation)
        => commands.Single(arguments => arguments.Count > 1 && arguments[1] == operation);
}
