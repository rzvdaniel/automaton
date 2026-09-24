using Automaton.Ai.Models;
using System.Text.Json;

namespace Automaton.Ai.Providers;

internal sealed class OpenAiCompatibleEventEmitter
{
    private readonly Action<ConversationUpdatedEvent> _publish;

    public OpenAiCompatibleEventEmitter(Action<ConversationUpdatedEvent> publish) => _publish = publish;

    public void EmitRetryNotification(string conversationId, int attempt, int maxRetries, int delaySeconds) =>
        _publish(new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [new ConversationMessage("assistant",
                $"⏳ Rate limit reached. Retrying in {delaySeconds}s (attempt {attempt}/{maxRetries})...")],
            ToolCalls = [],
            ToolResults = []
        });

    public void EmitStreamingText(
        string conversationId,
        string text,
        bool isComplete,
        int? inputTokens = null,
        int? outputTokens = null,
        string? model = null,
        string? reasoningContent = null,
        int? cacheReadTokens = null)
    {
        if (string.IsNullOrEmpty(text) &&
            (!isComplete || (!inputTokens.HasValue && !outputTokens.HasValue && !cacheReadTokens.HasValue)))
            return;

        var message = new ConversationMessage("assistant", text)
        {
            IsStreaming = !isComplete,
            InputTokens = isComplete ? inputTokens : null,
            OutputTokens = isComplete ? outputTokens : null,
            Model = isComplete ? model : null,
            ReasoningContent = isComplete ? reasoningContent : null,
            CacheReadTokens = isComplete ? cacheReadTokens : null
        };
        _publish(new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [message],
            ToolCalls = [],
            ToolResults = []
        });
    }

    public void EmitToolCallStarted(string conversationId, string toolCallId, string toolName, string argsJson) =>
        _publish(new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [],
            ToolCalls = [new ToolCall { Id = toolCallId, Name = toolName, Arguments = ParseArgs(argsJson) }],
            ToolResults = []
        });

    public void EmitToolCallCompleted(
        string conversationId,
        string toolCallId,
        string toolName,
        string argsJson,
        string result)
    {
        var isSuccess = !result.StartsWith("Tool execution failed:") && !result.StartsWith("Error executing tool");
        _publish(new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [],
            ToolCalls = [new ToolCall { Id = toolCallId, Name = toolName, Arguments = ParseArgs(argsJson) }],
            ToolResults = [new ToolResult
            {
                ToolCallId = toolCallId,
                Result = result,
                IsSuccess = isSuccess,
                ErrorMessage = isSuccess ? null : result
            }]
        });
    }

    private static Dictionary<string, object?> ParseArgs(string argsJson)
    {
        try
        {
            if (string.IsNullOrEmpty(argsJson) || argsJson == "{}")
                return [];
            return JsonDocument.Parse(argsJson).RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => (object?)property.Value.ToString());
        }
        catch
        {
            return [];
        }
    }
}
