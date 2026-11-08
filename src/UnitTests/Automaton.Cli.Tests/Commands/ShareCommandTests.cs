using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class ShareCommandTests
{
    [Test]
    public void Share_OffersEveryChoiceTheShareDialogHas()
    {
        var share = ShareCommand.Build(new ConfigService());
        Assert.That(share.Subcommands.Select(c => c.Name), Is.EquivalentTo(new[] { "status", "set", "add", "remove" }));
        Assert.That(share.Subcommands.Single(c => c.Name == "set").Arguments.Single().Description,
            Does.Contain("me").And.Contain("people").And.Contain("server").And.Contain("internet"));
    }
}
