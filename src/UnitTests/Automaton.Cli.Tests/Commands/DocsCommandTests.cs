using System.Text.Json;
using Automaton.Cli.Commands;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public sealed class DocsCommandTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Test]
    public void Finds_the_entry_page_in_either_file_list_shape()
    {
        Assert.That(DocsCommand.HasEntryPage(Parse("""["automaton.json","docs/README.md"]""")));
        Assert.That(DocsCommand.HasEntryPage(Parse("""{"files":["docs\\README.md"]}""")));
    }

    [Test]
    public void An_app_without_docs_readme_has_no_entry_page()
    {
        Assert.That(DocsCommand.HasEntryPage(Parse("""["automaton.json","README.md","docs/setup.md"]""")), Is.False);
        Assert.That(DocsCommand.HasEntryPage(Parse("{}")), Is.False);
    }

    [Test]
    public void Falls_back_to_the_description()
    {
        Assert.That(DocsCommand.Description(Parse("""{"description":"Does X."}""")), Is.EqualTo("Does X."));
        Assert.That(DocsCommand.Description(Parse("""{"description":null}""")), Does.Contain("no docs"));
    }
}
