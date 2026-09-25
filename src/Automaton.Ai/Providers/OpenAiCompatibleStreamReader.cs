using Automaton.Ai.Models;
using OpenAI.Chat;
using System.Text;
using System.Text.Json;

namespace Automaton.Ai.Providers;

internal sealed class OpenAiCompatibleStreamReader
{
    private readonly Action<ConversationUpdatedEvent> _publish;

    public OpenAiCompatibleStreamReader(Action<ConversationUpdatedEvent> publish) => _publish = publish;

    internal sealed record StreamResult(
        string finalText,
        string reasoningText,
        string? reasoningFieldName,
        List<ToolCallAccumulator> toolCalls,
        int? inputTokens,
        int? outputTokens,
        string? responseModel,
        int? cacheReadTokens);
    public async Task<StreamResult> StreamOnceAsync(
        ChatClient client,
        List<ChatMessage> messages,
        ChatCompletionOptions options,
        string conversationId,
        int streamingTimeoutMinutes,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(streamingTimeoutMinutes));
        var state = new StreamState(this, conversationId);
        var stream = client.CompleteChatStreamingAsync(messages, options, timeout.Token);
        await foreach (var chunk in stream.WithCancellation(timeout.Token))
            state.AddChunk(chunk);
        return state.ToResult();
    }

    private sealed class StreamState(OpenAiCompatibleStreamReader owner, string conversationId)
    {
        private readonly StringBuilder _text = new();
        private readonly StringBuilder _reasoning = new();
        private readonly Dictionary<int, ToolCallAccumulator> _toolCalls = new();
        private DateTime _lastEmitTime = DateTime.UtcNow;
        private int? _inputTokens;
        private int? _outputTokens;
        private int? _cacheReadTokens;
        private string? _responseModel;
        private string? _reasoningFieldName;

        public void AddChunk(StreamingChatCompletionUpdate chunk)
        {
            ReadAdditionalMetadata(chunk);
            AppendContent(chunk);
            AppendToolCalls(chunk);
            ReadStandardUsage(chunk);
        }

        public StreamResult ToResult() => new(
            _text.ToString(),
            _reasoning.ToString(),
            _reasoningFieldName,
            _toolCalls.Values.OrderBy(call => call.Index).ToList(),
            _inputTokens,
            _outputTokens,
            _responseModel,
            _cacheReadTokens);

        private void ReadAdditionalMetadata(StreamingChatCompletionUpdate chunk)
        {
            try
            {
                var rawBytes = System.ClientModel.Primitives.ModelReaderWriter.Write(
                    chunk, System.ClientModel.Primitives.ModelReaderWriterOptions.Json);
                using var document = JsonDocument.Parse(rawBytes);
                var root = document.RootElement;
                ReadReasoning(root);
                _cacheReadTokens = ReadCacheTokens(root) ?? _cacheReadTokens;
            }
            catch
            {
                // Provider-specific fields are absent from some stream chunks.
            }
        }

        private void ReadReasoning(JsonElement root)
        {
            if (!root.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0 ||
                !choices[0].TryGetProperty("delta", out var delta))
                return;

            if (TryReadReasoningField(delta, "reasoning_content", out var text))
                _reasoningFieldName = "reasoning_content";
            else if (TryReadReasoningField(delta, "reasoning", out text))
                _reasoningFieldName = "reasoning";
            else
                return;

            _reasoning.Append(text);
        }

        private static bool TryReadReasoningField(JsonElement delta, string propertyName, out string text)
        {
            text = string.Empty;
            if (!delta.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
                return false;
            text = value.GetString() ?? string.Empty;
            return !string.IsNullOrEmpty(text);
        }

        private static int? ReadCacheTokens(JsonElement root)
        {
            if (!root.TryGetProperty("usage", out var usage))
                return null;
            if (TryReadNumber(usage, "prompt_cache_hit_tokens", out var tokens))
                return tokens;
            if (usage.TryGetProperty("prompt_tokens_details", out var details) &&
                TryReadNumber(details, "cached_tokens", out tokens))
                return tokens;
            return null;
        }

        private static bool TryReadNumber(JsonElement element, string propertyName, out int value)
        {
            value = 0;
            return element.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.Number &&
                   property.TryGetInt32(out value);
        }

        private void AppendContent(StreamingChatCompletionUpdate chunk)
        {
            foreach (var part in chunk.ContentUpdate)
            {
                if (string.IsNullOrEmpty(part.Text))
                    continue;
                _text.Append(part.Text);
                EmitTextWhenDue();
            }
        }

        private void EmitTextWhenDue()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastEmitTime).TotalMilliseconds < 100)
                return;
            owner.EmitStreamingText(conversationId, _text.ToString(), isComplete: false);
            _lastEmitTime = now;
        }

        private void AppendToolCalls(StreamingChatCompletionUpdate chunk)
        {
            foreach (var update in chunk.ToolCallUpdates)
            {
                var accumulator = GetOrCreateToolCall(update.Index);
                UpdateToolCall(accumulator, update);
            }
        }

        private ToolCallAccumulator GetOrCreateToolCall(int index)
        {
            if (_toolCalls.TryGetValue(index, out var accumulator))
                return accumulator;
            accumulator = new ToolCallAccumulator { Index = index };
            _toolCalls.Add(index, accumulator);
            return accumulator;
        }

        private static void UpdateToolCall(
            ToolCallAccumulator accumulator,
            StreamingChatToolCallUpdate update)
        {
            if (!string.IsNullOrEmpty(update.ToolCallId))
                accumulator.Id = update.ToolCallId;
            if (!string.IsNullOrEmpty(update.FunctionName))
                accumulator.FunctionName = update.FunctionName;
            if (update.FunctionArgumentsUpdate is not null)
                accumulator.ArgumentsBuilder.Append(update.FunctionArgumentsUpdate);
        }

        private void ReadStandardUsage(StreamingChatCompletionUpdate chunk)
        {
            if (chunk.Usage is not null)
            {
                _inputTokens = chunk.Usage.InputTokenCount;
                _outputTokens = chunk.Usage.OutputTokenCount;
            }
            if (chunk.Model is not null)
                _responseModel = chunk.Model;
        }
    }
    private void EmitStreamingText(string conversationId, string text, bool isComplete)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var message = new ConversationMessage("assistant", text) { IsStreaming = !isComplete };
        _publish(new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [message],
            ToolCalls = [],
            ToolResults = []
        });
    }
    internal sealed class ToolCallAccumulator
    {
        public int Index { get; set; }
        public string Id { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
        public StringBuilder ArgumentsBuilder { get; } = new();
    }

}
