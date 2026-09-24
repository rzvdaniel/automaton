using Automaton.Ai.Models;
using Automaton.Ai.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Ai.Tests.Providers;

[TestFixture]
public class AnthropicProviderStreamingEmissionTests
{
    [Test]
    public void EmitStreamingTextUpdate_FinalEmptyTextWithTokens_ShouldEmitTokenOnlyMessage()
    {
        ConversationUpdatedEvent? captured = null;
        var emitter = new AnthropicConversationEventEmitter(NullLogger.Instance, updated => captured = updated);

        emitter.EmitStreamingTextUpdate(
            conversationId: "conv-1",
            text: string.Empty,
            isComplete: true,
            inputTokens: 110,
            outputTokens: 35,
            model: "claude-3-5-sonnet",
            cacheReadTokens: 90,
            cacheCreationTokens: 20);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Messages.Count, Is.EqualTo(1));

        var message = captured.Messages[0];
        Assert.That(message.Content, Is.EqualTo(string.Empty));
        Assert.That(message.IsStreaming, Is.False);
        Assert.That(message.InputTokens, Is.EqualTo(110));
        Assert.That(message.OutputTokens, Is.EqualTo(35));
        Assert.That(message.Model, Is.EqualTo("claude-3-5-sonnet"));
        Assert.That(message.CacheReadTokens, Is.EqualTo(90));
        Assert.That(message.CacheCreationTokens, Is.EqualTo(20));
    }

    [Test]
    public void EmitStreamingTextUpdate_FinalEmptyTextWithoutTokens_ShouldSkipEmission()
    {
        var eventRaised = false;
        var emitter = new AnthropicConversationEventEmitter(NullLogger.Instance, _ => eventRaised = true);

        emitter.EmitStreamingTextUpdate(
            conversationId: "conv-1",
            text: string.Empty,
            isComplete: true,
            inputTokens: null,
            outputTokens: null,
            model: "claude-3-5-sonnet",
            cacheReadTokens: null,
            cacheCreationTokens: null);

        Assert.That(eventRaised, Is.False);
    }

}
