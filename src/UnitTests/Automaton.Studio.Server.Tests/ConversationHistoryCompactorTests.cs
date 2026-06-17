using Automaton.Ai.Models;
using Automaton.Api.Config;
using Automaton.Api.Services;

namespace Automaton.Studio.Server.Tests;

[TestFixture]
public class ConversationHistoryCompactorTests
{
    [Test]
    public void Compact_WhenHistoryWithinLimit_ShouldReturnOriginalHistory()
    {
        var history = new List<ConversationMessage>
        {
            new("user", "hello"),
            new("assistant", "hi")
        };

        var compacted = ConversationHistoryCompactor.Compact(history, 10);

        Assert.That(compacted, Is.Not.Null);
        Assert.That(compacted!.Count, Is.EqualTo(2));
        Assert.That(compacted[0].Content, Is.EqualTo("hello"));
        Assert.That(compacted[1].Content, Is.EqualTo("hi"));
    }

    [Test]
    public void Compact_WhenHistoryExceedsLimit_ShouldAddSummaryAndKeepRecentMessages()
    {
        var history = new List<ConversationMessage>();
        for (var i = 1; i <= 20; i++)
        {
            history.Add(new ConversationMessage(i % 2 == 0 ? "assistant" : "user", $"message-{i}"));
        }

        var compacted = ConversationHistoryCompactor.Compact(history, 10);

        Assert.That(compacted, Is.Not.Null);
        Assert.That(compacted!.Count, Is.EqualTo(10));
        Assert.That(compacted[0].Content, Does.StartWith("Conversation summary of earlier turns"));
        Assert.That(compacted[^1].Content, Is.EqualTo("message-20"));
        Assert.That(compacted[^2].Content, Is.EqualTo("message-19"));
    }

    [Test]
    public void Compact_WithVerySmallLimit_ShouldKeepOnlyLatestMessages()
    {
        var history = new List<ConversationMessage>
        {
            new("user", "m1"),
            new("assistant", "m2"),
            new("user", "m3"),
            new("assistant", "m4")
        };

        var compacted = ConversationHistoryCompactor.Compact(history, 2);

        Assert.That(compacted, Is.Not.Null);
        Assert.That(compacted!.Count, Is.EqualTo(2));
        Assert.That(compacted[0].Content, Is.EqualTo("m3"));
        Assert.That(compacted[1].Content, Is.EqualTo("m4"));
    }

    [Test]
    public void Compact_ShouldRespectOperationalCompactionSettings()
    {
        var history = new List<ConversationMessage>();
        for (var i = 1; i <= 12; i++)
            history.Add(new ConversationMessage(i % 2 == 0 ? "assistant" : "user", $"long-message-{i}"));

        var compacted = ConversationHistoryCompactor.Compact(
            history,
            maxConversationHistoryMessages: 6,
            settings: new HistoryCompactionConfig
            {
                RecentMessages = 3,
                MaxSummaryEntries = 2,
                MaxSummaryChars = 120,
                MaxEntryChars = 20
            });

        Assert.That(compacted, Is.Not.Null);
        Assert.That(compacted!.Count, Is.EqualTo(4)); // 1 summary + 3 recent messages
        Assert.That(compacted[0].Content, Does.Contain("Conversation summary of earlier turns"));
        Assert.That(compacted[^1].Content, Is.EqualTo("long-message-12"));
    }

    [Test]
    public void Compact_WhenSettingsOutOfBounds_ShouldClampToSafeLimits()
    {
        var longOlderMessage = new string('x', 3000);
        var history = new List<ConversationMessage>
        {
            new("user", longOlderMessage),
            new("assistant", "mid"),
            new("assistant", "latest-1"),
            new("user", "latest-2")
        };

        var compacted = ConversationHistoryCompactor.Compact(
            history,
            maxConversationHistoryMessages: 3,
            settings: new HistoryCompactionConfig
            {
                RecentMessages = 9999,
                MaxSummaryEntries = 9999,
                MaxSummaryChars = 999999,
                MaxEntryChars = 999999
            });

        Assert.That(compacted, Is.Not.Null);
        Assert.That(compacted!.Count, Is.EqualTo(3));
        Assert.That(compacted[0].Content, Does.Contain("Conversation summary of earlier turns"));
        Assert.That(compacted[0].Content, Does.Not.Contain(new string('x', 1500)));
        Assert.That(compacted[1].Content, Is.EqualTo("latest-1"));
        Assert.That(compacted[2].Content, Is.EqualTo("latest-2"));
    }
}
