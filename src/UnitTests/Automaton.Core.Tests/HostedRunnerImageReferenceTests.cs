using Automaton.Core.Constants;

namespace Automaton.Core.Tests;

[TestFixture]
public sealed class HostedRunnerImageReferenceTests
{
    [TestCase("automaton/runner:latest")]
    [TestCase("automaton/runner:release")]
    [TestCase("")]
    [TestCase("automaton/runner@sha256:abc")]
    public void MutableOrMalformedReferenceIsRejected(string reference)
    {
        Assert.That(HostedRunnerImageReference.IsImmutable(reference), Is.False);
        Assert.Throws<InvalidOperationException>(() => HostedRunnerImageReference.EnsureImmutable(reference));
    }

    [Test]
    public void ImmutableDigestReferenceIsAccepted()
    {
        var reference = "automaton/runner@sha256:" + new string('a', 64);

        Assert.That(HostedRunnerImageReference.IsImmutable(reference), Is.True);
        Assert.DoesNotThrow(() => HostedRunnerImageReference.EnsureImmutable(reference));
    }
}
