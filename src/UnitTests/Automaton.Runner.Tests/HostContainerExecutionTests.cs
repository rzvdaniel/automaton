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
}
