using Automaton.Runner.Console.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

public sealed class InfrastructureCommandIdentityTests
{
    [Test]
    public void PersistedEnrollmentProfileOwnsHostAndPurpose()
    {
        var runnerId = Guid.NewGuid();
        var profile = new RunnerInstallationProfile { RunnerHostId = Guid.NewGuid(), Purpose = "Infrastructure" };
        var identity = InfrastructureCommandIdentity.Resolve(runnerId.ToString(), profile, "Standard", Guid.NewGuid().ToString());
        Assert.That(identity.RunnerId, Is.EqualTo(runnerId));
        Assert.That(identity.RunnerHostId, Is.EqualTo(profile.RunnerHostId));
    }

    [Test]
    public void ConfiguredIdentityWorksWithoutPersistedProfile()
    {
        var runnerId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        Assert.That(InfrastructureCommandIdentity.Resolve(runnerId.ToString(), null, "Infrastructure", hostId.ToString()),
            Is.EqualTo(new InfrastructureCommandIdentity(hostId, runnerId)));
    }

    [TestCase("Standard", true)]
    [TestCase("Infrastructure", false)]
    public void EnvironmentCannotOverrideIneligibleEnrollmentProfile(string purpose, bool hasHost)
    {
        var profile = new RunnerInstallationProfile { Purpose = purpose, RunnerHostId = hasHost ? Guid.NewGuid() : null };
        Assert.Throws<InvalidOperationException>(() => InfrastructureCommandIdentity.Resolve(
            Guid.NewGuid().ToString(), profile, "Infrastructure", Guid.NewGuid().ToString()));
    }

    [TestCase(null)]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public void MissingRunnerIdentityCannotConsume(string? runnerId)
    {
        Assert.Throws<InvalidOperationException>(() => InfrastructureCommandIdentity.Resolve(
            runnerId, null, "Infrastructure", Guid.NewGuid().ToString()));
    }
}
