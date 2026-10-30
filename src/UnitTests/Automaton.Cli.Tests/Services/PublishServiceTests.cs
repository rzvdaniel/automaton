using System.Text.Json;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class PublishServiceTests
{
    [Test]
    public void ReadCurrentListing_KeepsTheAppsCategoryAndTags()
    {
        using var app = JsonDocument.Parse("""{ "category": "Games", "tags": "game,words" }""");

        var listing = PublishService.ReadCurrentListing(app.RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(listing.Category, Is.EqualTo("Games"));
            Assert.That(listing.Tags, Is.EqualTo("game,words"));
        });
    }

    [Test]
    public void ReadCurrentListing_LeavesAnUnlistedAppEmpty()
    {
        using var app = JsonDocument.Parse("""{ "category": null }""");

        Assert.That(PublishService.ReadCurrentListing(app.RootElement), Is.EqualTo(((string?)null, (string?)null)));
    }
}
