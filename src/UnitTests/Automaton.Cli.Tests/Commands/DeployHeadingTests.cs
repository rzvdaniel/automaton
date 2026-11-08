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
}
