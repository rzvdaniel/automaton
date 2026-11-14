using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class CatalogUpdateCommandTests
{
    [Test]
    public void Update_TakesTheAppAndNoOptionThatCarriesASecretValue()
    {
        var update = CatalogUpdateCommand.Build(new ConfigService());

        Assert.That(update.Name, Is.EqualTo("update"));
        Assert.That(update.Arguments.Select(a => a.Name), Is.EqualTo(new[] { "app" }));
        Assert.That(update.Options.Select(o => o.Name), Is.EquivalentTo(
            new[] { "check", "confirm-edited", "secrets-file", "secrets-stdin", "wait", "json" }));
        Assert.That(update.Description, Does.Contain("--confirm-edited").And.Contain("--secrets-file"));
    }

    [Test]
    public void Catalog_HasTheUpdateCommand() =>
        Assert.That(CatalogCommand.Build(new ConfigService()).Subcommands.Select(c => c.Name), Does.Contain("update"));

    [Test]
    public void Short_CutsACommitToSevenCharacters()
    {
        Assert.That(CatalogUpdateCommand.Short("0123456789abcdef"), Is.EqualTo("0123456"));
        Assert.That(CatalogUpdateCommand.Short(""), Is.EqualTo(""));
    }
}
