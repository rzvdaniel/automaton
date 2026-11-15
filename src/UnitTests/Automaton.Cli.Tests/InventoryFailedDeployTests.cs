using NUnit.Framework;
using Automaton.Cli.Commands;

namespace Automaton.Cli.Tests;

[TestFixture]
public sealed class InventoryFailedDeployTests
{
    [Test]
    public void FailedDeployLine_GivesTheReason_AndSaysThePreviousVersionRunsOnlyWhenOneDoes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(InventoryCommands.DescribeFailedDeploy("Backup", "bad permission.", true),
                Is.EqualTo("\nBackup: the last deploy failed: bad permission. The app still runs the previous version."));
            Assert.That(InventoryCommands.DescribeFailedDeploy("Backup", "bad permission", false),
                Is.EqualTo("\nBackup: the last deploy failed: bad permission."));
        });
    }
}
