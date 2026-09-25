using Automaton.Ai.Models;
using Automaton.Ai.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Ai.Tests.Providers;

[TestFixture]
public class OpenAiProviderStreamingEmissionTests
{
    [Test]
    public void EmitStreamingTextUpdate_FinalEmptyTextWithTokens_ShouldEmitTokenOnlyMessage()
    {
        ConversationUpdatedEvent? captured = null;
        var emitter = new OpenAiConversationEventEmitter(NullLogger.Instance, e => captured = e);

        emitter.EmitStreamingTextUpdate(
            conversationId: "conv-1",
            text: string.Empty,
            isComplete: true,
            inputTokens: 123,
            outputTokens: 45,
            model: "gpt-4o");

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Messages.Count, Is.EqualTo(1));

        var message = captured.Messages[0];
        Assert.That(message.Content, Is.EqualTo(string.Empty));
        Assert.That(message.IsStreaming, Is.False);
        Assert.That(message.InputTokens, Is.EqualTo(123));
        Assert.That(message.OutputTokens, Is.EqualTo(45));
        Assert.That(message.Model, Is.EqualTo("gpt-4o"));
    }

    [Test]
    public void EmitStreamingTextUpdate_FinalEmptyTextWithoutTokens_ShouldSkipEmission()
    {
        var eventRaised = false;
        var emitter = new OpenAiConversationEventEmitter(NullLogger.Instance, _ => eventRaised = true);

        emitter.EmitStreamingTextUpdate(
            conversationId: "conv-1",
            text: string.Empty,
            isComplete: true,
            inputTokens: null,
            outputTokens: null,
            model: "gpt-4o");

        Assert.That(eventRaised, Is.False);
    }

}
