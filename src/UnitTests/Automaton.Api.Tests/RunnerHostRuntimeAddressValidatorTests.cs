using Automaton.Api.Services;

namespace Automaton.Api.Tests;

public sealed class RunnerHostRuntimeAddressValidatorTests
{
    [TestCase("0.1.2.3")]
    [TestCase("10.1.2.3")]
    [TestCase("100.64.0.1")]
    [TestCase("100.127.255.254")]
    [TestCase("127.0.0.1")]
    [TestCase("169.254.1.1")]
    [TestCase("172.16.0.1")]
    [TestCase("172.31.255.254")]
    [TestCase("192.0.0.10")]
    [TestCase("192.0.2.10")]
    [TestCase("192.88.99.10")]
    [TestCase("192.168.1.10")]
    [TestCase("198.18.0.1")]
    [TestCase("198.19.255.254")]
    [TestCase("198.51.100.10")]
    [TestCase("203.0.113.10")]
    [TestCase("224.0.0.1")]
    [TestCase("255.255.255.255")]
    public void RejectsSpecialPurposeRanges(string address)
    {
        var valid = RunnerHostRuntimeAddressValidator.TryValidatePublicIpv4(address, out var error);

        Assert.That(valid, Is.False);
        Assert.That(error, Does.Contain("publicly routable"));
    }

    [Test]
    public void AllowsPublicIpv4Address()
    {
        var valid = RunnerHostRuntimeAddressValidator.TryValidatePublicIpv4("8.8.8.8", out var error);

        Assert.That(valid, Is.True);
        Assert.That(error, Is.Empty);
    }
}
