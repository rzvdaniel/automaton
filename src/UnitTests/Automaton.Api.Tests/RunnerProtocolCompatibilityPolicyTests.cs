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

    private static Runner CreateRunner(string? version) => new()
    {
        Id = Guid.NewGuid(),
        Name = "protocol-test-runner",
        ApiCompatibilityVersion = version
    };
}
