using Automaton.Ai.Models;
using Automaton.Ai.Providers;

namespace Automaton.Ai.Tests.Providers;

[TestFixture]
public class GoogleProviderStreamingEmissionTests
{
    [Test]
    public void EmitStreamingTextUpdate_FinalEmptyTextWithTokens_ShouldEmitTokenOnlyMessage()
    {
        ConversationUpdatedEvent? captured = null;
        var emitter = new GoogleConversationEventEmitter(updated => captured = updated);

        emitter.EmitStreamingTextUpdate(
            conversationId: "conv-1",
            text: string.Empty,
            isComplete: true,
            inputTokens: 120,
            outputTokens: 30,
            model: "gemini-2.0-flash");

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Messages.Count, Is.EqualTo(1));

        var message = captured.Messages[0];
        Assert.That(message.Content, Is.EqualTo(string.Empty));
        Assert.That(message.IsStreaming, Is.False);
        Assert.That(message.InputTokens, Is.EqualTo(120));
        Assert.That(message.OutputTokens, Is.EqualTo(30));
        Assert.That(message.Model, Is.EqualTo("gemini-2.0-flash"));
    }

    [Test]
    public void EmitStreamingTextUpdate_FinalEmptyTextWithoutTokens_ShouldSkipEmission()
    {
        var eventRaised = false;
        var emitter = new GoogleConversationEventEmitter(_ => eventRaised = true);

        emitter.EmitStreamingTextUpdate(
            conversationId: "conv-1",
            text: string.Empty,
            isComplete: true,
            inputTokens: null,
            outputTokens: null,
            model: "gemini-2.0-flash");

        Assert.That(eventRaised, Is.False);
    }

}
