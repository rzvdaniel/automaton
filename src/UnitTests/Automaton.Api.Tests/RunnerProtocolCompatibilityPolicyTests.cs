using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Constants;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RunnerProtocolCompatibilityPolicyTests
{
    [TestCase(RunnerApiCompatibilityVersions.ManagedApplicationArtifacts)]
    [TestCase(RunnerApiCompatibilityVersions.Current)]
    public void CurrentProtocolVersionIsAccepted(string version)
    {
        Assert.DoesNotThrow(() => RunnerProtocolCompatibilityPolicy.EnsureSupportsManagedApplicationArtifacts(CreateRunner(version)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-a-version")]
    [TestCase(RunnerApiCompatibilityVersions.Legacy)]
    public void MissingMalformedOrOldProtocolVersionIsRejected(string? version)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RunnerProtocolCompatibilityPolicy.EnsureSupportsManagedApplicationArtifacts(CreateRunner(version)));

        Assert.That(exception!.Message, Does.Contain("Upgrade the Runner before deploying"));
        Assert.That(exception.Message, Does.Not.Contain("api-key"));
    }

    [TestCase(RunnerApiCompatibilityVersions.ManagedApplicationArtifacts)]
    [TestCase(RunnerApiCompatibilityVersions.Legacy)]
    [TestCase(null)]
    public void ContainerApplicationExecutionRequiresProtocolThree(string? version)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RunnerProtocolCompatibilityPolicy.EnsureSupportsContainerApplicationExecution(CreateRunner(version)));

        Assert.That(exception!.Message, Does.Contain("container-based application execution"));
    }

    [Test]
    public void ContainerApplicationExecutionAcceptsProtocolThree()
    {
        Assert.DoesNotThrow(() =>
            RunnerProtocolCompatibilityPolicy.EnsureSupportsContainerApplicationExecution(
                CreateRunner(RunnerApiCompatibilityVersions.ContainerApplicationExecution)));
    }

    [TestCase(RunnerApiCompatibilityVersions.RuntimeTunnelReconnectCommands, true)]
    [TestCase(RunnerApiCompatibilityVersions.HostContainerExecution, false)]
    [TestCase(null, false)]
    public void RuntimeTunnelReconnectCommandCompatibilityRequiresProtocolFive(string? version, bool expected)
    {
        Assert.That(RunnerProtocolCompatibilityPolicy.SupportsRuntimeTunnelReconnectCommands(version),
            Is.EqualTo(expected));
    }

    [TestCase(RunnerApiCompatibilityVersions.AppTunnelOverHttp2, true)]
    [TestCase(RunnerApiCompatibilityVersions.RuntimeTunnelReconnectCommands, false)]
    [TestCase(null, false)]
    public void AppTunnelCompatibilityRequiresProtocolSix(string? version, bool expected)
    {
        Assert.That(RunnerProtocolCompatibilityPolicy.SupportsAppTunnelOverHttp2(version), Is.EqualTo(expected));
    }

    private static Runner CreateRunner(string? version) => new()
    {
        Id = Guid.NewGuid(),
        Name = "protocol-test-runner",
        ApiCompatibilityVersion = version
    };

    [TestCase("0.2.166", "security:read", true)]
    [TestCase("0.2.172", "security:manage", true)]
    [TestCase("0.2.167", "logs:receive", true)]
    [TestCase("0.2.173", "security:manage", false)]
    [TestCase("0.2.173", "logs:receive", false)]
    [TestCase("0.2.1", "backups:read", false)]
    [TestCase(null, "security:manage", false)]
    [TestCase("not-a-version", "security:manage", false)]
    public void RunnerReleaseDecidesIfAPermissionIsKnown(string? release, string permission, bool tooOld)
    {
        var runner = CreateRunner(RunnerApiCompatibilityVersions.Current);
        runner.Version = release;
        if (!tooOld)
        {
            Assert.DoesNotThrow(() => RunnerProtocolCompatibilityPolicy.EnsureSupportsPermissions(runner, ["backups:read", permission]));
            return;
        }
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RunnerProtocolCompatibilityPolicy.EnsureSupportsPermissions(runner, ["backups:read", permission]));
        Assert.That(exception!.Message, Does.Contain("Runner too old").And.Contain(permission).And.Contain("Upgrade").And.Contain(release));
        Assert.That(exception.Message, Does.Not.Contain("backups:read"));
    }
}
