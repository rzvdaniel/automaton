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
}
