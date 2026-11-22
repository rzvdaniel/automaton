using Automaton.Cli.Models;
using NUnit.Framework;
using System.Text.Json;

namespace Automaton.Cli.Tests.Models;

[TestFixture]
public class CliAppDeclarationTests
{
    [Test]
    public void From_ReadsKindDescriptionPagesAndOpenApi()
    {
        using var document = JsonDocument.Parse("""
            {"application":{"kind":"api","description":"Orders","openApi":"/openapi.json",
             "pages":[{"path":"/","label":"Home"}]}}
            """);

        var result = CliAppDeclaration.From(document.RootElement);

        Assert.That(result.Kind, Is.EqualTo("api"));
        Assert.That(result.Description, Is.EqualTo("Orders"));
        Assert.That(result.OpenApi, Is.EqualTo("/openapi.json"));
        Assert.That(result.Pages, Is.EqualTo(new[] { new CliAppPage("/", "Home") }));
    }

    [Test]
    public void From_DefaultsToWebWhenNothingIsDeclared()
    {
        using var document = JsonDocument.Parse("""{"application":{}}""");

        var result = CliAppDeclaration.From(document.RootElement);

        Assert.That(result.Kind, Is.EqualTo("web"));
        Assert.That(result.Description, Is.Null);
        Assert.That(result.Pages, Is.Empty);
        Assert.That(result.OpenApi, Is.Null);
    }
}
