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
        DateTimeOffset.UtcNow,
        new DeploymentTargetRunnerInfo("runner-1", "Hosted Runner", true));

    private static readonly DeploymentTargetInfo NamedTarget = new(
        "target-eu",
        "Server EU",
        "server-eu",
        false,
        "Running",
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
            Is.EqualTo("Default (default) · Live · Automaton Hosted · Running"));
    }

    [Test]
    public void GetDeployableTargets_AllowsOnlyLiveBootstrapWithoutRunner()
    {
        var liveBootstrap = DefaultTarget with { Runner = null };
        var liveOnline = DefaultTarget with { Runner = new DeploymentTargetRunnerInfo("runner-1", "Runner", true) };
        var liveOffline = DefaultTarget with { Runner = new DeploymentTargetRunnerInfo("runner-1", "Runner", false) };
        var liveDeploying = DefaultTarget with { Status = "Deploying", Runner = new DeploymentTargetRunnerInfo("runner-1", "Runner", true) };
        var additionalUnassigned = NamedTarget with { Runner = null };
        var additionalOffline = NamedTarget with { Runner = new DeploymentTargetRunnerInfo("runner-2", "Runner", false) };

        var deployable = DeploymentTargetService.GetDeployableTargets([
            liveBootstrap, liveOnline, liveOffline, liveDeploying, additionalUnassigned, additionalOffline]);

        Assert.That(deployable, Is.EquivalentTo(new[] { liveBootstrap, liveOnline }));
    }

    [Test]
    public void SelectEnvironment_AllowsInspectionOfFailedOrOfflineEnvironment()
    {
        var offline = NamedTarget with
        {
            Status = "RunnerOffline",
            Runner = new DeploymentTargetRunnerInfo("runner-2", "Runner", false)
        };

        Assert.That(
            DeploymentTargetService.SelectEnvironment([offline], null, nonInteractive: true),
            Is.EqualTo(offline));
    }

    [Test]
    public void Format_ShowsNoRuntimeAssignedWithoutRunner()
    {
        Assert.That(
            DeploymentTargetService.Format(NamedTarget with { Runner = null }),
            Does.Contain("No runtime assigned"));
    }

    [Test]
    public void SelectEnvironment_AllowsUnassignedAdditionalForRunnerAssignment()
    {
        var unassigned = NamedTarget with { Runner = null };

        Assert.That(
            DeploymentTargetService.SelectEnvironment([unassigned], unassigned.Slug, nonInteractive: true),
            Is.EqualTo(unassigned));
    }

    [Test]
    public void SelectEnvironment_NonInteractiveRequiresExplicitEnvironmentWhenSeveralExist()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DeploymentTargetService.SelectEnvironment([DefaultTarget, NamedTarget], null, nonInteractive: true));

        Assert.That(exception!.Message, Does.Contain("default, server-eu"));
        Assert.That(exception.Message, Does.Not.Contain("automaton logs"));
    }
}
