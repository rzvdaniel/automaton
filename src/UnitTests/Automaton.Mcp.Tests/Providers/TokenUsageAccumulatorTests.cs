using Automaton.Mcp.Client.Providers;

namespace Automaton.Mcp.Tests.Providers;

[TestFixture]
public class TokenUsageAccumulatorTests
{
    [Test]
    public void Add_ShouldAccumulateInputOutputAndCacheTokens()
    {
        var sut = new TokenUsageAccumulator();

        sut.Add(inputTokens: 100, outputTokens: 25, cacheReadTokens: 80, cacheCreationTokens: 10);
        sut.Add(inputTokens: 40, outputTokens: 5, cacheReadTokens: 20, cacheCreationTokens: 0);

        Assert.That(sut.InputTokens, Is.EqualTo(140));
        Assert.That(sut.OutputTokens, Is.EqualTo(30));
        Assert.That(sut.OutputTokensOrNull, Is.EqualTo(30));
        Assert.That(sut.CacheReadTokens, Is.EqualTo(100));
        Assert.That(sut.CacheCreationTokens, Is.EqualTo(10));
        Assert.That(sut.HasAny, Is.True);
    }

    [Test]
    public void Add_WithOnlyNulls_ShouldKeepAccumulatorEmpty()
    {
        var sut = new TokenUsageAccumulator();

        sut.Add(inputTokens: null, outputTokens: null, cacheReadTokens: null, cacheCreationTokens: null);

        Assert.That(sut.InputTokens, Is.Null);
        Assert.That(sut.OutputTokens, Is.EqualTo(0));
        Assert.That(sut.OutputTokensOrNull, Is.Null);
        Assert.That(sut.CacheReadTokens, Is.Null);
        Assert.That(sut.CacheCreationTokens, Is.Null);
        Assert.That(sut.HasAny, Is.False);
    }
}
