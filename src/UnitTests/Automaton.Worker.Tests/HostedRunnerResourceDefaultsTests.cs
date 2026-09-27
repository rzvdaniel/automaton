using Automaton.Api.Models;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class HostedRunnerResourceDefaultsTests
{
    [Test]
    public void FreeSpecificationDefaultsMatchHostedRunnerPlatformContract()
    {
        var resources = new HostedRunnerResources();
        Assert.Multiple(() =>
        {
            Assert.That(resources.CpuLimit, Is.EqualTo(0.50m));
            Assert.That(resources.MemoryLimitMb, Is.EqualTo(384));
            Assert.That(resources.DiskLimitMb, Is.EqualTo(512));
            Assert.That(resources.WritableLayerLimitMb, Is.EqualTo(512));
            Assert.That(resources.PidsLimit, Is.EqualTo(256));
            Assert.That(resources.SharedMemoryMb, Is.EqualTo(64));
            Assert.That(resources.DockerLogMaxSizeMb, Is.EqualTo(10));
            Assert.That(resources.DockerLogMaxFiles, Is.EqualTo(3));
        });
    }
}
