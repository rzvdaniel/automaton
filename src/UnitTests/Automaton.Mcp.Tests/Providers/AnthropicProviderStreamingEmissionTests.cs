using System.Reflection;
using Automaton.Mcp.Client.Providers;
using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Mcp.Tests.Providers;

[TestFixture]
public class AnthropicProviderStreamingEmissionTests
{
    [Test]
    public void EmitStreamingTextUpdate_FinalEmptyTextWithTokens_ShouldEmitTokenOnlyMessage()
    {
        var provider = new AnthropicProvider(NullLogger.Instance);
        ConversationUpdatedEvent? captured = null;
        provider.ConversationUpdated += (_, e) => captured = e;

        InvokeEmitStreamingTextUpdate(provider,
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
        var provider = new AnthropicProvider(NullLogger.Instance);
        var eventRaised = false;
        provider.ConversationUpdated += (_, _) => eventRaised = true;

        InvokeEmitStreamingTextUpdate(provider,
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

    private static void InvokeEmitStreamingTextUpdate(
        AnthropicProvider provider,
        string conversationId,
        string text,
        bool isComplete,
        int? inputTokens,
        int? outputTokens,
        string? model,
        int? cacheReadTokens,
        int? cacheCreationTokens)
    {
        var method = typeof(AnthropicProvider).GetMethod(
            "EmitStreamingTextUpdate",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null, "Could not find EmitStreamingTextUpdate via reflection");

        method!.Invoke(provider, [conversationId, text, isComplete, inputTokens, outputTokens, model, cacheReadTokens, cacheCreationTokens]);
    }
}
