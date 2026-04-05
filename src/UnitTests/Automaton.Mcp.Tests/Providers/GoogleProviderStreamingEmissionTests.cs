using System.Reflection;
using Automaton.Mcp.Client.Providers;
using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Mcp.Tests.Providers;

[TestFixture]
public class GoogleProviderStreamingEmissionTests
{
    [Test]
    public void EmitStreamingTextUpdate_FinalEmptyTextWithTokens_ShouldEmitTokenOnlyMessage()
    {
        var provider = new GoogleProvider(NullLogger.Instance);
        ConversationUpdatedEvent? captured = null;
        provider.ConversationUpdated += (_, e) => captured = e;

        InvokeEmitStreamingTextUpdate(provider,
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
        var provider = new GoogleProvider(NullLogger.Instance);
        var eventRaised = false;
        provider.ConversationUpdated += (_, _) => eventRaised = true;

        InvokeEmitStreamingTextUpdate(provider,
            conversationId: "conv-1",
            text: string.Empty,
            isComplete: true,
            inputTokens: null,
            outputTokens: null,
            model: "gemini-2.0-flash");

        Assert.That(eventRaised, Is.False);
    }

    private static void InvokeEmitStreamingTextUpdate(
        GoogleProvider provider,
        string conversationId,
        string text,
        bool isComplete,
        int? inputTokens,
        int? outputTokens,
        string? model)
    {
        var method = typeof(GoogleProvider).GetMethod(
            "EmitStreamingTextUpdate",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, "Could not find EmitStreamingTextUpdate via reflection");

        method!.Invoke(provider, [conversationId, text, isComplete, inputTokens, outputTokens, model]);
    }
}
