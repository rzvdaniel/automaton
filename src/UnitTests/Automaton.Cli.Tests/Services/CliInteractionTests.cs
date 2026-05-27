using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class CliInteractionTests
{
    private string? _originalCi;
    private string? _originalAutomatonCi;

    [SetUp]
    public void SetUp()
    {
        _originalCi = Environment.GetEnvironmentVariable("CI");
        _originalAutomatonCi = Environment.GetEnvironmentVariable("AUTOMATON_CI");
        Environment.SetEnvironmentVariable("CI", null);
        Environment.SetEnvironmentVariable("AUTOMATON_CI", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("CI", _originalCi);
        Environment.SetEnvironmentVariable("AUTOMATON_CI", _originalAutomatonCi);
    }

    [Test]
    public void IsNonInteractive_WhenExplicitFlagIsSet_ReturnsTrue()
    {
        Assert.That(CliInteraction.IsNonInteractive(explicitNonInteractive: true), Is.True);
    }

    [Test]
    public void IsNonInteractive_WhenJsonModeIsSet_ReturnsTrue()
    {
        Assert.That(CliInteraction.IsNonInteractive(explicitNonInteractive: false, asJson: true), Is.True);
    }

    [Test]
    public void IsNonInteractive_WhenCiEnvironmentVariableIsTrue_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("CI", "true");

        Assert.That(CliInteraction.IsNonInteractive(explicitNonInteractive: false), Is.True);
    }

    [Test]
    public void IsNonInteractive_WhenAutomatonCiEnvironmentVariableIsTrue_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_CI", "1");

        Assert.That(CliInteraction.IsNonInteractive(explicitNonInteractive: false), Is.True);
    }
}
