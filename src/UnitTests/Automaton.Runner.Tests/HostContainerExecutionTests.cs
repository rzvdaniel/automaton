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
        var service = new HostContainerExecutionService((arguments, _) =>
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
        var service = new HostContainerExecutionService((_, _) =>
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
        var service = new HostContainerExecutionService((_, _) =>
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
        var commands = new List<IReadOnlyList<string>>();
        var hostedId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var service = new HostContainerExecutionService((arguments, _) =>
        {
            commands.Add(arguments);
            var output = arguments.Contains("inspect") && arguments.Contains("image")
                ? "example/build:1@sha256:abc"
                : arguments.Contains("volume")
                    ? $"{{\"automaton.managed\":\"true\",\"automaton.resource\":\"hosted-runner-volume\",\"automaton.hosted-runner-id\":\"{hostedId}\",\"automaton.storage-scope\":\"user\"}}"
                    : "";
            return Task.FromResult(new InfrastructureCommandResponse { Success = true, Output = output });
        });
        var priorPurpose = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE");
        var priorCapabilities = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES");
        try
        {
            var scope = new HostContainerScope(hostedId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", RunnerPurposes.Infrastructure);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", RunnerCapabilities.HostContainerExecution);
            var result = await service.ExecuteAsync(new HostContainerExecutionRequest
            {
                Scope = scope,
                Operation = HostContainerOperationKinds.RunBuild,
                RequestedImage = "example/build:1",
                Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact, HostContainerResourceKinds.ApplicationData),
                Command = new() { Executable = "build-tool", Arguments = ["build"] },
                Environment = new Dictionary<string, string> { ["BUILD_MODE"] = "test" }
            });

            Assert.That(result.Success, Is.True);
            Assert.That(commands.Any(x => x.Contains("volume") && x.Contains("automaton-hosted-runner-00000000000000000000000000000001-user")), Is.True);
            var build = string.Join(' ', commands.Single(x => string.Join(' ', x).Contains("runtime-kind=build")));
            Assert.That(build, Does.Contain("volume-subpath=generic-runtime/apps/"));
            Assert.That(build, Does.Contain("--cap-drop"));
            Assert.That(build, Does.Not.Contain("/var/run/docker.sock"));
            Assert.That(build, Does.Not.Contain("/host"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE", priorPurpose);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES", priorCapabilities);
        }
    }

    [Test]
    public async Task HostedRunnerCannotUseHostContainerCapability()
    {
        var service = new HostContainerExecutionService((_, _) =>
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
        var service = new HostContainerExecutionService((_, _) =>
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
