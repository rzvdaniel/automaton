using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class HostContainerExecutionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task LegacyOrMixedAccountScopeIsRejectedBeforeDocker(bool includeMachineIdentity)
    {
        using var machine = MachineContainerTestEnvironment.Create();
        var invoked = false;
        var service = new HostContainerExecutionService((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(new InfrastructureCommandResponse { Success = true });
        });
        var result = await service.ExecuteAsync(new HostContainerExecutionRequest
        {
            Scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                MachineRunnerId: includeMachineIdentity ? machine.RunnerId : Guid.Empty),
            Operation = HostContainerOperationKinds.ResolveImage, RequestedImage = "example/app:1"
        });
        Assert.That(result.ErrorCode, Is.EqualTo("scope"));
        Assert.That(invoked, Is.False);
    }

    [TestCase("HostContainerExecution")]
    [TestCase(RunnerCapabilities.ManagedContainerExecution)]
    public async Task InfrastructureIdentityCannotExecuteMachineOrLegacyContainerWork(string capabilities)
    {
        using var machine = MachineContainerTestEnvironment.Create();
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", "Infrastructure");
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", capabilities);
        var invoked = false;
        var service = new HostContainerExecutionService((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(new InfrastructureCommandResponse { Success = true });
        });
        var result = await service.ExecuteAsync(new HostContainerExecutionRequest
        {
            Scope = MachineContainerTestEnvironment.CurrentScope(),
            Operation = HostContainerOperationKinds.ResolveImage, RequestedImage = "example/app:1"
        });
        Assert.That(result.ErrorCode, Is.EqualTo("authorization"));
        Assert.That(invoked, Is.False);
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
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", "Infrastructure");
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", "HostExecution");
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                Operation = HostContainerOperationKinds.ResolveImage,
                RequestedImage = "python:3.13-slim"
            });

            Assert.That(result.ErrorCode, Is.EqualTo("authorization"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", priorPurpose);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", priorCapabilities);
        }
    }

    [Test]
    public async Task MachineManagementRunsTypedOperationOnlyForItsStableIdentity()
        => await MachineManagementIdentityScenario.ExecuteAsync();

    private static class MachineManagementIdentityScenario
    {
        public static async Task ExecuteAsync()
        {
            var commands = new List<IReadOnlyList<string>>();
            var service = new HostContainerExecutionService((arguments, _, _) =>
            {
                commands.Add(arguments);
                return Task.FromResult(new InfrastructureCommandResponse
                {
                    Success = true,
                    Output = arguments.Contains("pull") ? "pulled" : "python:3.13-slim@sha256:abc"
                });
            });
            using var machine = MachineContainerTestEnvironment.Create();
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = new(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), MachineRunnerId: machine.RunnerId),
                Operation = HostContainerOperationKinds.ResolveImage,
                RequestedImage = "python:3.13-slim",
                MinimumFreeDiskMb = 1024
            });

            Assert.That(result.Success, Is.True);
            Assert.That(result.ResolvedImage, Is.EqualTo("python:3.13-slim@sha256:abc"));
            Assert.That(commands, Has.Count.EqualTo(2));

            var mismatch = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = new(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), MachineRunnerId: Guid.NewGuid()),
                Operation = HostContainerOperationKinds.ResolveImage,
                RequestedImage = "python:3.13-slim",
                MinimumFreeDiskMb = 1024
            });
            Assert.That(mismatch.ErrorCode, Is.EqualTo("scope"));
            Assert.That(commands, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task MachineBuildWithoutServerDerivedContainerLimitsFailsClosed()
    {
        var invoked = false;
        var runnerId = Guid.NewGuid();
        var previous = new Dictionary<string, string?>
        {
            ["AUTOMATON_RUNNER_PURPOSE"] = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"),
            ["AUTOMATON_RUNNER_APPLICATION_PROFILE"] = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"),
            ["AUTOMATON_RUNNER_CAPABILITIES"] = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES"),
            ["AUTOMATON_RUNNER_ID"] = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_ID"),
            ["AUTOMATON_RUNNER_INSTALLATION_ID"] = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID")
        };
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Managed);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", RunnerApplicationProfiles.MachineManagement);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.ManagedContainerExecution);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_ID", runnerId.ToString());
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID", "sh_" + Guid.NewGuid().ToString("N"));
            var result = await new HostContainerExecutionService((_, _, _) =>
            {
                invoked = true;
                return Task.FromResult(new InfrastructureCommandResponse { Success = true });
            }).ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = new(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), runnerId),
                Operation = HostContainerOperationKinds.RunBuild,
                RequestedImage = "example/build:1",
                Command = new() { Executable = "build" },
                Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact,
                    HostContainerResourceKinds.ApplicationData),
                SharedMemoryMb = 64, DockerLogMaxSizeMb = 20, DockerLogMaxFiles = 5
            });

            Assert.That(result.ErrorCode, Is.EqualTo("trusted_policy_unavailable"));
            Assert.That(invoked, Is.False);
        }
        finally
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    [Test]
    public void MachineWorkspaceVolumeIsBoundToItsInstallationAndOwnershipLabels()
    {
        var previousInstallationId = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID");
        var installationId = "sh_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID", installationId);
        try
        {
            var scope = new HostContainerScope(
                Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), MachineRunnerId: Guid.NewGuid());
            var labels = $$"""{"automaton.managed":"true","automaton.resource":"machine-workspace-volume","automaton.installation-id":"{{installationId}}","automaton.storage-scope":"workspace"}""";

            Assert.Multiple(() =>
            {
                Assert.That(HostContainerResourcePolicy.VolumeName(scope),
                    Is.EqualTo($"automaton-machine-runner-{installationId}-workspace"));
                Assert.That(HostContainerResourcePolicy.HasExpectedVolumeLabels(labels, scope), Is.True);
                Assert.That(HostContainerResourcePolicy.HasExpectedVolumeLabels(
                    labels.Replace(installationId, "sh_" + Guid.NewGuid().ToString("N"), StringComparison.Ordinal), scope), Is.False);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID", previousInstallationId);
        }
    }

    [Test]
    public async Task ProductionServicePathUsesDedicatedCapabilityWithoutHostExecution()
    {
        using var machine = MachineContainerTestEnvironment.Create();
        using var testWorkspace = RunnerRuntimeContainerTestWorkspace.Create();
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
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Managed);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.ManagedContainerExecution);
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = MachineContainerTestEnvironment.CurrentScope(),
                Operation = HostContainerOperationKinds.ResolveImage,
                MinimumFreeDiskMb = 256, RequestedImage = "python:3.13-slim"
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
        using var machine = MachineContainerTestEnvironment.Create();
        using var testWorkspace = RunnerRuntimeContainerTestWorkspace.Create();
        var service = new HostContainerExecutionService((_, _, _) =>
            Task.FromResult(new InfrastructureCommandResponse { Success = true }));
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var priorCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Managed);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.ManagedContainerExecution);
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = MachineContainerTestEnvironment.CurrentScope(),
                Operation = HostContainerOperationKinds.RunBuild,
                MinimumFreeDiskMb = 256, RequestedImage = "python:3.13-slim",
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
    public async Task RunBuildUsesMachineVolumeAndTrustedSubpaths()
    {
        var result = await HostContainerExecutionScenarios.RunLabeledMachineVolumeBuildAsync();

        Assert.That(result.Success, Is.True);
        Assert.That(result.VolumeNameRequested, Is.True);
        Assert.That(result.BuildCommand, Does.Not.Contain("--cgroup-parent"));
        Assert.That(result.BuildCommand, Does.Contain("volume-subpath=workspace/runner/deployments/"));
        Assert.That(result.BuildCommand, Does.Contain(
            $"volume-subpath=workspace/runner/deployments/{result.AppId:D}/{result.DeploymentTargetId:D}/{result.DeploymentAttemptId:D}/source"));
        Assert.That(result.BuildCommand, Does.Contain("--cap-drop"));
        Assert.That(result.BuildCommand, Does.Contain("--user 1000:1000"));
        Assert.That(result.BuildCommand, Does.Not.Contain("DAC_OVERRIDE"));
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
    public async Task RunBuildPreservesDeterministicContainerForRetryWhenHostCommandThrows()
    {
        var outcome = await HostContainerExecutionScenarios.RunBuildWithHostCommandExceptionAsync();

        Assert.That(outcome.CancellationSurfaced, Is.True);
        Assert.That(outcome.CleanupWasRequested, Is.False);
        Assert.That(outcome.RetrySucceeded, Is.True);
        Assert.That(outcome.RunAttempts, Is.EqualTo(1));
        Assert.That(outcome.WaitRequested, Is.True);
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
        => Assert.That(await HostContainerExecutionScenarios.ProductionTypedDockerPathReceivesDockerPrefixedRunBuildAsync(), Is.True);

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
        using var machine = MachineContainerTestEnvironment.Create();
        using var testWorkspace = RunnerRuntimeContainerTestWorkspace.Create();
        var service = new HostContainerExecutionService((_, _, _) =>
            Task.FromResult(new InfrastructureCommandResponse { Success = true }));
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var priorCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Managed);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.ManagedContainerExecution);
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = MachineContainerTestEnvironment.CurrentScope(),
                Operation = HostContainerOperationKinds.CreateApplicationContainer,
                MinimumFreeDiskMb = 256, RequestedImage = "python:3.13-slim",
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

    [Test]
    public async Task CreateApplicationContainerAppliesValidatedRuntimeUser()
    {
        var outcome = await HostContainerRuntimeUserScenario.RunAsync();
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Success, Is.True, outcome.Error);
            Assert.That(outcome.UserApplied, Is.True);
        });
    }
}
