using System.Text.Json;
using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class NotificationsCommandTests
{
    [Test]
    public void Format_ListsSeverityTitleKeyAndDetail()
    {
        using var doc = JsonDocument.Parse("""[{"key":"runner-offline","severity":"critical","title":"Runner x is offline","detail":"Apps cannot run."}]""");
        Assert.That(NotificationsCommand.Format(doc.RootElement), Is.EqualTo(
            "[critical] Runner x is offline (runner-offline)" + Environment.NewLine + "    Apps cannot run."));
    }

    [Test]
    public void Format_SaysWhenThereIsNothing()
    {
        using var doc = JsonDocument.Parse("[]");
        Assert.That(NotificationsCommand.Format(doc.RootElement), Is.EqualTo("No notifications."));
    }

    [Test]
    public void TheCommandTakesJsonAndAnAcknowledgeSubcommand()
    {
        var command = NotificationsCommand.Build(new ConfigService());
        Assert.That(command.Name, Is.EqualTo("notifications"));
        Assert.That(command.Options.Select(option => option.Name), Has.Member("json"));
        Assert.That(command.Subcommands.Select(sub => sub.Name), Is.EqualTo(new[] { "acknowledge" }));
    }
}
