using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class DeploymentTargetServiceTests
{
    private static readonly DeploymentTargetInfo DefaultTarget = new(
        "target-default",
        "Default",
        "default",
        true,
        "Running",
        true,
        true,
        DateTimeOffset.UtcNow,
        new DeploymentTargetRunnerInfo("runner-1", "Hosted Runner", true));

    private static readonly DeploymentTargetInfo NamedTarget = new(
        "target-eu",
        "Server EU",
        "server-eu",
        false,
        "Running",
        true,
        true,
        DateTimeOffset.UtcNow,
        new DeploymentTargetRunnerInfo("runner-2", "Runner EU", true));

    [Test]
    public void Select_WithoutSlugReturnsDefault()
    {
        Assert.That(
            DeploymentTargetService.Select([NamedTarget, DefaultTarget], null),
            Is.EqualTo(DefaultTarget));
    }

    [Test]
    public void Select_WithSlugNormalizesAndReturnsNamedTarget()
    {
        Assert.That(
            DeploymentTargetService.Select([DefaultTarget, NamedTarget], " SERVER-EU "),
            Is.EqualTo(NamedTarget));
    }

    [Test]
    public void Select_UnknownSlugListsAvailableTargets()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DeploymentTargetService.Select([DefaultTarget, NamedTarget], "missing"));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("missing"));
            Assert.That(exception.Message, Does.Contain("default (default)"));
            Assert.That(exception.Message, Does.Contain("server-eu"));
        });
    }
}
