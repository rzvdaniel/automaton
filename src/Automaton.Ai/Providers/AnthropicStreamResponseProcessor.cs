using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json.Nodes;

namespace Automaton.Ai.Providers;

internal delegate void AnthropicStreamingTextUpdate(
    string conversationId,
    string text,
    bool isComplete,
    int? inputTokens = null,
    int? outputTokens = null,
    string? model = null,
    int? cacheReadTokens = null,
    int? cacheCreationTokens = null);

internal sealed class AnthropicStreamResponseProcessor(
    ILogger logger,
    AnthropicStreamingTextUpdate emitStreamingTextUpdate)
{
    public async Task<StreamResult> ProcessAsync(
        AnthropicClient client,
        MessageParameters messageParams,
        string conversationId,
        CancellationToken cancellationToken,
        int? accumulatedInputTokens = null,
        int accumulatedOutputTokens = 0,
        int? accumulatedCacheReadTokens = null,
        int? accumulatedCacheCreationTokens = null)
    {
        var result = new StreamResult { Model = messageParams.Model };
        var currentText = new StringBuilder();
        var toolUseBlocks = new Dictionary<int, ToolUseInfo>();
        var state = new StreamState();

        await CollectStreamAsync(client, messageParams, conversationId, cancellationToken,
            result, currentText, toolUseBlocks, state);

        var finalText = currentText.ToString();
        var tokenUsage = GetFinalTokenUsage(
            result, accumulatedInputTokens, accumulatedOutputTokens,
            accumulatedCacheReadTokens, accumulatedCacheCreationTokens);
        EmitFinalContent(conversationId, finalText, result.Model, tokenUsage, result);
        LogCacheStats(result);
        AddToolUseResults(result, toolUseBlocks);
        return result;
    }

    private async Task CollectStreamAsync(
        AnthropicClient client,
        MessageParameters messageParams,
        string conversationId,
        CancellationToken cancellationToken,
        StreamResult result,
        StringBuilder currentText,
        Dictionary<int, ToolUseInfo> toolUseBlocks,
        StreamState state)
    {
        try
        {
            await foreach (var streamEvent in client.Messages.StreamClaudeMessageAsync(messageParams, cancellationToken))
            {
                ProcessStreamEvent(streamEvent, conversationId, result, currentText, toolUseBlocks, state);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during Anthropic streaming: {Message}", ex.Message);
            throw;
        }
    }

    private void ProcessStreamEvent(
        MessageResponse streamEvent,
        string conversationId,
        StreamResult result,
        StringBuilder currentText,
        Dictionary<int, ToolUseInfo> toolUseBlocks,
        StreamState state)
    {
        CaptureUsage(streamEvent, result);
        AppendTextDelta(streamEvent, conversationId, currentText, state);
        CaptureToolUseBlock(streamEvent, toolUseBlocks, state);
        AppendToolInputDelta(streamEvent, toolUseBlocks, state);
        if (streamEvent.Delta?.StopReason is { } stopReason)
            result.StopReason = stopReason;
    }

    private static void CaptureUsage(MessageResponse streamEvent, StreamResult result)
    {
        if (streamEvent.Usage is null) return;
        result.InputTokens = streamEvent.Usage.InputTokens;
        result.OutputTokens = streamEvent.Usage.OutputTokens;
        result.CacheCreationTokens = streamEvent.Usage.CacheCreationInputTokens;
        result.CacheReadTokens = streamEvent.Usage.CacheReadInputTokens;
    }

    private void AppendTextDelta(
        MessageResponse streamEvent,
        string conversationId,
        StringBuilder currentText,
        StreamState state)
    {
        if (streamEvent.Delta?.Text is not { } text) return;
        currentText.Append(text);
        var now = DateTime.UtcNow;
        if ((now - state.LastEmitTime).TotalMilliseconds < 100) return;
        emitStreamingTextUpdate(conversationId, currentText.ToString(), isComplete: false);
        state.LastEmitTime = now;
    }

    private static void CaptureToolUseBlock(
        MessageResponse streamEvent,
        Dictionary<int, ToolUseInfo> toolUseBlocks,
        StreamState state)
    {
        var block = streamEvent.ContentBlock;
        if (block is null || block.Type != "tool_use") return;
        toolUseBlocks[state.CurrentToolIndex] = new ToolUseInfo
        {
            Index = state.CurrentToolIndex,
            Id = block.Id ?? "",
            Name = block.Name ?? "",
            InputBuilder = new StringBuilder()
        };
        state.CurrentToolIndex++;
    }

    private static void AppendToolInputDelta(
        MessageResponse streamEvent,
        Dictionary<int, ToolUseInfo> toolUseBlocks,
        StreamState state)
    {
        if (streamEvent.Delta?.PartialJson is not { } partialJson) return;
        var lastToolIndex = state.CurrentToolIndex - 1;
        if (lastToolIndex >= 0 && toolUseBlocks.TryGetValue(lastToolIndex, out var toolInfo))
            toolInfo.InputBuilder.Append(partialJson);
    }

    private static FinalTokenUsage GetFinalTokenUsage(
        StreamResult result,
        int? accumulatedInputTokens,
        int accumulatedOutputTokens,
        int? accumulatedCacheReadTokens,
        int? accumulatedCacheCreationTokens)
    {
        if (result.StopReason == "tool_use") return FinalTokenUsage.Empty;
        return new FinalTokenUsage(
            NonZero((accumulatedInputTokens ?? 0) + (result.InputTokens ?? 0)),
            NonZero(accumulatedOutputTokens + (result.OutputTokens ?? 0)),
            NonZero((accumulatedCacheReadTokens ?? 0) + (result.CacheReadTokens ?? 0)),
            NonZero((accumulatedCacheCreationTokens ?? 0) + (result.CacheCreationTokens ?? 0)));
    }

    private void EmitFinalContent(
        string conversationId,
        string finalText,
        string? model,
        FinalTokenUsage usage,
        StreamResult result)
    {
        if (!string.IsNullOrWhiteSpace(finalText))
        {
            emitStreamingTextUpdate(conversationId, finalText, isComplete: true,
                inputTokens: usage.Input, outputTokens: usage.Output, model: model,
                cacheReadTokens: usage.CacheRead, cacheCreationTokens: usage.CacheCreation);
            result.TextContent = finalText;
            return;
        }

        if (!usage.HasAny) return;
        emitStreamingTextUpdate(conversationId, string.Empty, isComplete: true,
            inputTokens: usage.Input, outputTokens: usage.Output, model: model,
            cacheReadTokens: usage.CacheRead, cacheCreationTokens: usage.CacheCreation);
    }

    private void LogCacheStats(StreamResult result)
    {
        if (result.CacheReadTokens > 0 || result.CacheCreationTokens > 0)
        {
            logger.LogDebug(
                "Anthropic cache stats — read: {CacheRead} tokens, creation: {CacheCreation} tokens (saved ~{Saved} tokens from billing)",
                result.CacheReadTokens ?? 0,
                result.CacheCreationTokens ?? 0,
                result.CacheReadTokens ?? 0);
            return;
        }
        logger.LogDebug("Anthropic cache stats — no cache hit this request (creation: {CacheCreation})",
            result.CacheCreationTokens ?? 0);
    }

    private void AddToolUseResults(StreamResult result, Dictionary<int, ToolUseInfo> toolUseBlocks)
    {
        foreach (var toolInfo in toolUseBlocks.Values)
            result.ToolUseBlocks.Add(new ToolUseBlockInfo(toolInfo.Id, toolInfo.Name, ParseToolInput(toolInfo)));
    }

    private JsonNode? ParseToolInput(ToolUseInfo toolInfo)
    {
        var inputJson = toolInfo.InputBuilder.ToString();
        if (string.IsNullOrEmpty(inputJson)) return null;
        try
        {
            return JsonNode.Parse(inputJson);
        }
        catch
        {
            logger.LogWarning("Failed to parse tool input JSON: {Json}", inputJson);
            return null;
        }
    }

    private static int? NonZero(int value) => value > 0 ? value : null;

    internal sealed class StreamResult
    {
        public string? TextContent { get; set; }
        public string? StopReason { get; set; }
        public List<ToolUseBlockInfo> ToolUseBlocks { get; } = [];
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public string? Model { get; set; }
        public int? CacheCreationTokens { get; set; }
        public int? CacheReadTokens { get; set; }
    }

    internal sealed record ToolUseBlockInfo(string Id, string Name, JsonNode? Input);

    private sealed class ToolUseInfo
    {
        public int Index { get; init; }
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public StringBuilder InputBuilder { get; init; } = new();
    }

    private sealed record StreamState
    {
        public int CurrentToolIndex { get; set; }
        public DateTime LastEmitTime { get; set; } = DateTime.UtcNow;
    }

    private readonly record struct FinalTokenUsage(int? Input, int? Output, int? CacheRead, int? CacheCreation)
    {
        public static FinalTokenUsage Empty => new(null, null, null, null);
        public bool HasAny => Input.HasValue || Output.HasValue || CacheRead.HasValue || CacheCreation.HasValue;
    }
}