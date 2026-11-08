using Automaton.Cli.Commands;
using Automaton.Cli.Models;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class DeployHeadingTests
{
    [TestCase(null, false)]
    [TestCase("live", false)]
    [TestCase("work", true)]
    public void ShowProfile_HidesOnlyTheDefaultProfile(string? profile, bool shown)
        => Assert.That(DeployCommand.ShowProfile(profile), Is.EqualTo(shown));

    [TestCase("Live", "live", "Live")]
    [TestCase("Staging", "stage", "Staging (stage)")]
    public void TargetLabel_RepeatsTheSlugOnlyWhenItDiffers(string name, string slug, string expected)
        => Assert.That(DeployCommand.TargetLabel(new DeploymentTargetInfo("id", name, slug, true, "ok", null, null)), Is.EqualTo(expected));
}
