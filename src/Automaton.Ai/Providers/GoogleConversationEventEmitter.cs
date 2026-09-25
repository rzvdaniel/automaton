using Automaton.Ai.Models;
using System.Text.Json;

namespace Automaton.Ai.Providers;

internal sealed class GoogleConversationEventEmitter
{
    private readonly Action<ConversationUpdatedEvent> _publish;

    public GoogleConversationEventEmitter(Action<ConversationUpdatedEvent> publish) => _publish = publish;

    public void EmitRetryNotification(string conversationId, int attempt, int maxRetries, int delaySeconds) =>
        Publish(conversationId, message: new ConversationMessage("assistant",
            $"⏳ Rate limit reached. Retrying in {delaySeconds}s (attempt {attempt}/{maxRetries})..."));

    public void EmitStreamingTextUpdate(
        string conversationId,
        string text,
        bool isComplete,
        int? inputTokens = null,
        int? outputTokens = null,
        string? model = null)
    {
        if (string.IsNullOrEmpty(conversationId) ||
            (string.IsNullOrEmpty(text) && (!isComplete || (!inputTokens.HasValue && !outputTokens.HasValue))))
            return;

        var message = new ConversationMessage("assistant", text)
        {
            IsStreaming = !isComplete,
            InputTokens = isComplete ? inputTokens : null,
            OutputTokens = isComplete ? outputTokens : null,
            Model = isComplete ? model : null
        };
        Publish(conversationId, message: message);
    }

    public void EmitToolCallStarted(string conversationId, string toolCallId, string toolName, string argsJson) =>
        Publish(conversationId, toolCall: CreateToolCall(toolCallId, toolName, argsJson));

    public void EmitToolCallCompleted(
        string conversationId,
        string toolCallId,
        string toolName,
        string argsJson,
        string result)
    {
        var failed = IsToolFailure(result);
        var toolResult = new ToolResult
        {
            ToolCallId = toolCallId,
            Result = result,
            IsSuccess = !failed,
            ErrorMessage = failed ? result : null
        };
        Publish(conversationId, toolCall: CreateToolCall(toolCallId, toolName, argsJson), toolResult: toolResult);
    }

    private void Publish(
        string conversationId,
        ConversationMessage? message = null,
        ToolCall? toolCall = null,
        ToolResult? toolResult = null)
    {
        _publish(new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = message is null ? [] : [message],
            ToolCalls = toolCall is null ? [] : [toolCall],
            ToolResults = toolResult is null ? [] : [toolResult]
        });
    }

    private static ToolCall CreateToolCall(string id, string name, string arguments) =>
        new() { Id = id, Name = name, Arguments = ParseToolArguments(arguments) };

    private static bool IsToolFailure(string result) =>
        result.StartsWith("Tool execution failed:") || result.StartsWith("Error executing tool");

    private static Dictionary<string, object?> ParseToolArguments(string argsJson)
    {
        try
        {
            if (string.IsNullOrEmpty(argsJson) || argsJson == "{}")
                return new Dictionary<string, object?>();
            return JsonDocument.Parse(argsJson).RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => (object?)property.Value.ToString());
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }
}
