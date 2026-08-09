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
            Assert.That(exception.Message, Does.Contain("default (Live)"));
            Assert.That(exception.Message, Does.Contain("server-eu"));
        });
    }

    [Test]
    public void SelectForDeploy_UsesOnlyTargetWithoutPrompting()
    {
        Assert.That(
            DeploymentTargetService.SelectForDeploy([NamedTarget], null, nonInteractive: true),
            Is.EqualTo(NamedTarget));
    }

    [Test]
    public void SelectForDeploy_NonInteractiveRequiresExplicitTargetWhenSeveralAreAvailable()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DeploymentTargetService.SelectForDeploy([DefaultTarget, NamedTarget], null, nonInteractive: true));

        Assert.That(exception!.Message, Does.Contain("automaton deploy default"));
        Assert.That(exception.Message, Does.Contain("automaton deploy server-eu"));
    }

    [Test]
    public void Format_ShowsLiveRoleDestinationAndStatus()
    {
        var hosted = DefaultTarget with
        {
            Runner = new DeploymentTargetRunnerInfo("runner-1", "Hosted Runner", true, IsHostedRunner: true)
        };

        Assert.That(
            DeploymentTargetService.Format(hosted),
            Is.EqualTo("default · Live · Automaton Hosted · Running"));
    }
}
