using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class HostContainerExecutionTests
{
    [Test]
    public async Task ResolveImageWorksWithDedicatedCapabilityWithoutHostExecution()
    {
        var commands = new List<string>();
        var service = new HostContainerExecutionService((arguments, _, _) =>
        {
            commands.Add(string.Join(' ', arguments));
            return Task.FromResult(new InfrastructureCommandResponse
            {
                Success = true,
                Output = commands.Count == 2 ? "python:3.13-slim@sha256:abc" : "pulled"
            });
        });
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var priorCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Infrastructure);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.HostContainerExecution);
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                Operation = HostContainerOperationKinds.ResolveImage,
                RequestedImage = "python:3.13-slim"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True);
                Assert.That(result.ResolvedImage, Is.EqualTo("python:3.13-slim@sha256:abc"));
                Assert.That(commands, Has.Count.EqualTo(2));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", priorPurpose);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", priorCapabilities);
        }
    }

    [Test]
    public async Task HostExecutionAloneDoesNotAuthorizeHostContainerExecution()
    {
        var service = new HostContainerExecutionService((_, _, _) =>
            Task.FromResult(new InfrastructureCommandResponse { Success = true }));
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var priorCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Infrastructure);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.HostExecution);
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                Operation = HostContainerOperationKinds.ResolveImage,
                RequestedImage = "python:3.13-slim"
            });

            Assert.That(result.ErrorCode, Is.EqualTo("capability"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", priorPurpose);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", priorCapabilities);
        }
    }

    [Test]
    public async Task ProductionServicePathUsesDedicatedCapabilityWithoutHostExecution()
    {
        var invoked = false;
        var infrastructureCommands = new InfrastructureCommandService((arguments, _, _) =>
        {
            invoked = true;
            if (arguments[1] == "pull")
                Assert.That(arguments, Is.EqualTo(new[] { "docker", "pull", "python:3.13-slim" }));
            else
                Assert.That(arguments, Does.Contain("inspect"));
            return Task.FromResult(new InfrastructureCommandResponse { Success = true, Output = "pulled" });
        });
        var service = new HostContainerExecutionService(infrastructureCommands);
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var priorCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Infrastructure);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.HostContainerExecution);
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                Operation = HostContainerOperationKinds.ResolveImage,
                RequestedImage = "python:3.13-slim"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo("image_digest"));
                Assert.That(invoked, Is.True);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", priorPurpose);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", priorCapabilities);
        }
    }

    [Test]
    public async Task StorageDependentOperationFailsWithMappingError()
    {
        var service = new HostContainerExecutionService((_, _, _) =>
            Task.FromResult(new InfrastructureCommandResponse { Success = true }));
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var priorCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Infrastructure);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.HostContainerExecution);
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                Operation = HostContainerOperationKinds.RunBuild,
                RequestedImage = "python:3.13-slim",
                Command = new()
            });

            Assert.That(result.ErrorCode, Is.EqualTo("trusted_mapping_unavailable"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", priorPurpose);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", priorCapabilities);
        }
    }

    [Test]
    public async Task RunBuildUsesLabeledHostedVolumeAndTrustedSubpaths()
    {
        var result = await HostContainerExecutionScenarios.RunLabeledHostedVolumeBuildAsync();

        Assert.That(result.Success, Is.True);
        Assert.That(result.VolumeNameRequested, Is.True);
        Assert.That(result.BuildCommand, Does.Contain($"--cgroup-parent automaton-hosted-runner-{result.HostedRunnerId:N}.slice"));
        Assert.That(result.BuildCommand, Does.Contain("volume-subpath=workspace/runner/deployments/"));
        Assert.That(result.BuildCommand, Does.Contain(
            $"volume-subpath=workspace/runner/deployments/{result.AppId:D}/{result.DeploymentTargetId:D}/{result.DeploymentAttemptId:D}/source"));
        Assert.That(result.BuildCommand, Does.Contain("--cap-drop"));
        Assert.That(result.BuildCommand, Does.Contain("--read-only"));
        Assert.That(result.BuildCommand, Does.Contain("/tmp:rw,nosuid,nodev,size=32m,mode=1777"));
        Assert.That(result.BuildCommand, Does.Contain("--shm-size 64m"));
        Assert.That(result.BuildCommand, Does.Contain("--log-driver local"));
        Assert.That(result.BuildCommand, Does.Contain("max-size=10m"));
        Assert.That(result.BuildCommand, Does.Contain("max-file=3"));
        Assert.That(result.BuildCommand, Does.Not.Contain("readonly"));
        Assert.That(result.BuildCommand, Does.Not.Contain("/var/run/docker.sock"));
        Assert.That(result.BuildCommand, Does.Not.Contain("/host"));
    }

    [Test]
    public async Task RunBuildCleansUpContainerWhenHostCommandThrows()
    {
        var outcome = await HostContainerExecutionScenarios.RunBuildWithHostCommandExceptionAsync();

        Assert.That(outcome.CancellationSurfaced, Is.True);
        Assert.That(outcome.CleanupWasRequested, Is.True);
    }

    [Test]
    public async Task RunBuildPlacesEntrypointBeforeImageForPathShellAndSourceRelativeCommands()
    {
        var result = await HostContainerExecutionScenarios.RunEntrypointVariantsAsync();

        Assert.That(result.PathSucceeded, Is.True);
        Assert.That(result.SourceRelativeSucceeded, Is.True);
        Assert.That(result.ShellSucceeded, Is.True);
        Assert.That(result.PathCommand.Skip(result.PathEntryPoint).Take(4), Is.EqualTo(new[] { "--entrypoint", "dotnet", "example/build:1@sha256:abc", "publish" }));
        Assert.That(result.PathCommand, Does.Contain("-c"));
        Assert.That(result.PathCommand, Does.Contain("Release"));
        Assert.That(result.SourceRelativeCommand.Skip(result.SourceRelativeEntryPoint).Take(3), Is.EqualTo(new[] { "--entrypoint", "/automaton/source/scripts/build.sh", "example/build:1@sha256:abc" }));
        Assert.That(result.ShellCommand.Skip(result.ShellEntryPoint).Take(3), Is.EqualTo(new[] { "--entrypoint", "/bin/sh", "example/build:1@sha256:abc" }));
        Assert.That(result.ShellCommand, Does.Contain("-c"));
    }

    [Test]
    public async Task ProductionTypedDockerPathReceivesDockerPrefixedRunBuild()
    {
        var calls = new List<IReadOnlyList<string>>();
        var infrastructure = new InfrastructureCommandService((arguments, _, _) =>
        {
            calls.Add(arguments);
            var output = arguments.Contains("volume")
                ? "{\"automaton.managed\":\"true\",\"automaton.resource\":\"hosted-runner-volume\",\"automaton.hosted-runner-id\":\"00000000-0000-0000-0000-000000000001\",\"automaton.storage-scope\":\"user\"}"
                : arguments.Contains("image") ? "example/build:1@sha256:abc" : "";
            return Task.FromResult(new InfrastructureCommandResponse { Success = true, Output = output });
        });
        var service = new HostContainerExecutionService(infrastructure);
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var priorCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Infrastructure);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.HostContainerExecution);
            await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = new(Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                Operation = HostContainerOperationKinds.RunBuild,
                RequestedImage = "example/build:1",
                Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact, HostContainerResourceKinds.ApplicationData),
                SharedMemoryMb = 64, DockerLogMaxSizeMb = 10, DockerLogMaxFiles = 3,
                Command = new() { Executable = "build" }
            });

            Assert.That(calls.Any(call => call.Count > 1 && call[0] == "docker" && call[1] == "run"), Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", priorPurpose);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", priorCapabilities);
        }
    }

    [Test]
    public void BrokerStagingSubpathMatchesRunnerWorkspaceStagingSource()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var priorRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", Path.Combine(Path.GetTempPath(), "automaton-broker-parity"));
            var workspace = new RunnerWorkspaceService();
            var staging = workspace.GetDeploymentStagingDirectory(appId, targetId, attemptId);
            var relative = Path.GetRelativePath(workspace.WorkspaceRoot, Path.Combine(staging, "source"));
            Assert.That(relative.Replace('\\', '/'), Is.EqualTo(
                $"runner/deployments/{appId:D}/{targetId:D}/{attemptId:D}/source"));
            Assert.That(RunnerWorkspaceService.GetDeploymentStagingVolumeSubpath(appId, targetId, attemptId), Is.EqualTo(
                $"workspace/runner/deployments/{appId:D}/{targetId:D}/{attemptId:D}"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", priorRoot);
        }
    }

    [Test]
    public async Task HostedRunnerCannotUseHostContainerCapability()
    {
        var service = new HostContainerExecutionService((_, _, _) =>
            Task.FromResult(new InfrastructureCommandResponse { Success = true }));
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Managed);
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                Operation = HostContainerOperationKinds.ResolveImage,
                Resources = new("workspace", "artifact", "data"),
                RequestedImage = "python:3.13-slim"
            });

            Assert.That(result.ErrorCode, Is.EqualTo("authorization"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", priorPurpose);
        }
    }

    [Test]
    public async Task SuppliedResourceIdsDoNotEstablishTrustedMapping()
    {
        var service = new HostContainerExecutionService((_, _, _) =>
            Task.FromResult(new InfrastructureCommandResponse { Success = true }));
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var priorCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Infrastructure);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.HostContainerExecution);
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                Operation = HostContainerOperationKinds.CreateApplicationContainer,
                RequestedImage = "python:3.13-slim",
                Command = new(),
                SharedMemoryMb = 64, DockerLogMaxSizeMb = 10, DockerLogMaxFiles = 3,
                Resources = new("caller-workspace", "caller-artifact", "caller-data")
            });

            Assert.That(result.ErrorCode, Is.EqualTo("trusted_mapping_unavailable"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", priorPurpose);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", priorCapabilities);
        }
    }
}
