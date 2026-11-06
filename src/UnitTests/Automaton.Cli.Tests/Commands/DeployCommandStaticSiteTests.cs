using Automaton.Cli.Commands;
using Automaton.Cli.Models;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class DeployCommandStaticSiteTests
{
    [Test]
    public void ResultRewrites_KeepTheStaticSiteFlag()
    {
        var result = new DeployResult { Success = true, IsStaticSite = true };

        Assert.That(DeployCommand.AppendDefaultSuccessNextSteps(result).IsStaticSite, Is.True);
        Assert.That(DeployCommand.ApplyTargetIdentity(result, new DeploymentTargetInfo("1", "Live", "live", true, "Active", null, null)).IsStaticSite, Is.True);
    }
}
