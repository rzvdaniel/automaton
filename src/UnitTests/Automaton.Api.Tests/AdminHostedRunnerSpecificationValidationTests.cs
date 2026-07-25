using Automaton.Api.Controllers.Admin;
using Automaton.Api.Models;
using NUnit.Framework;

namespace Automaton.Api.Tests;

public sealed class AdminHostedRunnerSpecificationValidationTests
{
    [TestCase(nameof(HostedRunnerResources.CpuLimit), "CPU limit")]
    [TestCase(nameof(HostedRunnerResources.MemoryLimitMb), "Memory limit")]
    [TestCase(nameof(HostedRunnerResources.PidsLimit), "PID limit")]
    [TestCase(nameof(HostedRunnerResources.SharedMemoryMb), "Shared memory limit")]
    [TestCase(nameof(HostedRunnerResources.DockerLogMaxSizeMb), "Docker log size limit")]
    [TestCase(nameof(HostedRunnerResources.DockerLogMaxFiles), "Docker log file count")]
    public void ValidateRejectsNonPositiveOrOutOfRangeDockerLimits(string propertyName, string expectedMessage)
    {
        var resources = new HostedRunnerResources();
        object invalidValue = propertyName == nameof(HostedRunnerResources.CpuLimit)
            ? (object)0m
            : 0;
        typeof(HostedRunnerResources).GetProperty(propertyName)!.SetValue(resources, invalidValue);

        var validation = AdminHostedRunnerSpecificationsController.Validate(
            "test",
            "Test",
            resources);

        Assert.That(validation, Does.Contain(expectedMessage));
    }
}
