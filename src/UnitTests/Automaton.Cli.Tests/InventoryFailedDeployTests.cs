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

[TestFixture]
public sealed class CatalogUpdateFailureTextTests
{
    [Test]
    public void FailureText_NamesTheRunningCommit_ThePreviousVersion_OrNothingRunning()
    {
        var recorded = CatalogUpdateCommand.FailureText("App", "boom", "1234567890", true, "abcdefg123", "app");
        var legacy = CatalogUpdateCommand.FailureText("App", "boom", "", true, null, "app");
        var never = CatalogUpdateCommand.FailureText("App", "boom", "", false, null, "app");
        Assert.Multiple(() =>
        {
            Assert.That(recorded, Does.Contain("It still runs 1234567.").And.Contain("Retry the deploy of abcdefg with: automaton catalog update app"));
            Assert.That(legacy, Does.Contain("It still runs the previous version.").And.Not.Contain("Retry"));
            Assert.That(never, Does.Contain("Nothing is running yet."));
        });
    }
}

[TestFixture]
public sealed class PausedReasonControlCharacterTests
{
    [Test]
    public void StripControlCharacters_DropsEscapesAndBells_ButKeepsNewlinesAndText()
    {
        Assert.Multiple(() =>
        {
            Assert.That(InventoryCommands.StripControlCharacters("a\u001b[2Jb\u0007c\r\td\ne"), Is.EqualTo("a[2Jbcd\ne"));
            Assert.That(InventoryCommands.StripControlCharacters("Billing overdue"), Is.EqualTo("Billing overdue"));
        });
    }
}
