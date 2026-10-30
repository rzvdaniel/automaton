using System.Text.Json;
using Automaton.Cli.Commands;
using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class ListingCommandTests
{
    [Test]
    public void ListingCommand_ExposesTheListingSubcommands()
    {
        var command = ListingCommand.Build(new ConfigService());

        Assert.That(
            command.Subcommands.Select(item => item.Name),
            Is.EquivalentTo(new[] { "show", "set", "cover", "add-screenshot", "remove-screenshot" }));
    }

    [Test]
    public void ReadListing_ParsesTextTagsCoverAndOrderedScreenshots()
    {
        using var document = JsonDocument.Parse("""
            {
              "name": "Untranslatable",
              "summary": "A bluffing game",
              "description": "Real words, fake meanings.",
              "category": "Games",
              "tags": "game, multiplayer",
              "thumbnailUrl": "/app-thumbnails/app/cover.png",
              "screenshots": [
                { "id": "second", "imageUrl": "/b.png", "sortOrder": 2 },
                { "id": "first", "imageUrl": "/a.png", "sortOrder": 1 }
              ]
            }
            """);

        var listing = MarketplaceListingService.ReadListing("app-1", document.RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(listing.Summary, Is.EqualTo("A bluffing game"));
            Assert.That(listing.Category, Is.EqualTo("Games"));
            Assert.That(listing.Tags, Is.EqualTo(new[] { "game", "multiplayer" }));
            Assert.That(listing.CoverUrl, Is.EqualTo("/app-thumbnails/app/cover.png"));
            Assert.That(listing.Screenshots.Select(shot => shot.Id), Is.EqualTo(new[] { "first", "second" }));
        });
    }

    [Test]
    public void ValidateImage_RejectsMissingUnsupportedAndOversizedFiles()
    {
        var directory = Directory.CreateTempSubdirectory("automaton-listing-");
        try
        {
            var text = Path.Combine(directory.FullName, "notes.txt");
            File.WriteAllText(text, "not an image");
            var large = Path.Combine(directory.FullName, "large.png");
            File.WriteAllBytes(large, new byte[MarketplaceListingService.MaximumImageBytes + 1]);
            var small = Path.Combine(directory.FullName, "cover.png");
            File.WriteAllBytes(small, [0x89, 0x50, 0x4E, 0x47]);

            Assert.Multiple(() =>
            {
                Assert.That(() => MarketplaceListingService.ValidateImage(Path.Combine(directory.FullName, "missing.png")),
                    Throws.InvalidOperationException.With.Message.Contains("not found"));
                Assert.That(() => MarketplaceListingService.ValidateImage(text),
                    Throws.InvalidOperationException.With.Message.Contains("Unsupported"));
                Assert.That(() => MarketplaceListingService.ValidateImage(large),
                    Throws.InvalidOperationException.With.Message.Contains("2 MB"));
                Assert.That(MarketplaceListingService.ValidateImage(small), Is.EqualTo(Path.GetFullPath(small)));
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public void SetAsync_WithNothingToChange_ExplainsWhichOptionsToPass()
    {
        var service = new MarketplaceListingService(new ConfigService());

        Assert.That(
            async () => await service.SetAsync(Directory.GetCurrentDirectory(), null, new MarketplaceListingUpdate()),
            Throws.InvalidOperationException.With.Message.Contains("--summary"));
    }

    [Test]
    public void RemoveScreenshotAsync_RejectsSomethingThatIsNotAnId()
    {
        var service = new MarketplaceListingService(new ConfigService());

        Assert.That(
            async () => await service.RemoveScreenshotAsync(Directory.GetCurrentDirectory(), null, "cover.png"),
            Throws.InvalidOperationException.With.Message.Contains("automaton listing show"));
    }

    [Test]
    public void ListingUpdate_SendsOnlyTheListingFields()
    {
        var json = JsonSerializer.Serialize(new MarketplaceListingUpdate(Summary: "A word game"));

        Assert.That(json, Does.Not.Contain("IsEmpty"));
    }
}
