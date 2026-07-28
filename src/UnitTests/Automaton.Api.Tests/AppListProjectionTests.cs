using Automaton.Api.Services;

namespace Automaton.Api.Tests;

public sealed class AppListProjectionTests
{
    [Test]
    public void ParsesPresentationTagsWithoutNormalizingStoredValue()
    {
        Assert.That(AppsService.ParseTags(" games, strategy ,, "), Is.EqualTo(new[] { "games", "strategy" }));
        Assert.That(AppsService.ParseTags(null), Is.Empty);
    }

    [Test]
    public void NormalizesTagsWithStableFirstSeenCasingAndOrder()
    {
        var tags = AppTagNormalizer.Normalize(" Games, games, GAMES, strategy ,, Games ");

        Assert.That(tags, Is.EqualTo(new[] { "Games", "strategy" }));
        Assert.That(AppTagNormalizer.Serialize(tags), Is.EqualTo("Games,strategy"));
    }

    [Test]
    public void RejectsTagLimits()
    {
        Assert.Throws<InvalidOperationException>(() => AppTagNormalizer.Normalize(new string('x', AppTagNormalizer.MaximumTagLength + 1)));
        Assert.Throws<InvalidOperationException>(() => AppTagNormalizer.Normalize(Enumerable.Range(0, AppTagNormalizer.MaximumTagsPerApp + 1).Select(index => $"tag-{index}")));
    }
}
