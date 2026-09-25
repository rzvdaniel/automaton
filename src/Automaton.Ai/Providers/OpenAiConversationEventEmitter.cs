using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Automaton.Ai.Providers;

internal sealed class OpenAiConversationEventEmitter
{
    private readonly ILogger _logger;
    private readonly Action<ConversationUpdatedEvent> _publish;

    public OpenAiConversationEventEmitter(ILogger logger, Action<ConversationUpdatedEvent> publish)
    {
        _logger = logger;
        _publish = publish;
    }

    public void EmitToolCallStarted(string conversationId, string toolCallId, string toolName, string argsJson)
    {
        var currentToolCalls = new List<ToolCall>
        {
            new() { Id = toolCallId, Name = toolName, Arguments = ParseToolArguments(argsJson) }
        };
        _publish(new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [],
            ToolCalls = currentToolCalls,
            ToolResults = []
        });
    }

    public void EmitToolCallCompleted(
        string conversationId,
        string toolCallId,
        string toolName,
        string argsJson,
        string result)
    {
        var currentToolCalls = new List<ToolCall>
        {
            new() { Id = toolCallId, Name = toolName, Arguments = ParseToolArguments(argsJson) }
        };
        var isFailure = result.StartsWith("Tool execution failed:") || result.StartsWith("Error executing tool");
        var toolResult = new ToolResult
        {
            ToolCallId = toolCallId,
            Result = result,
            IsSuccess = !isFailure,
            ErrorMessage = isFailure ? result : null
        };
        _publish(new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [],
            ToolCalls = currentToolCalls,
            ToolResults = [toolResult]
        });
    }

    public void EmitStreamingTextUpdate(
        string conversationId,
        string text,
        bool isComplete,
        int? inputTokens = null,
        int? outputTokens = null,
        string? model = null)
    {
        try
        {
            if (string.IsNullOrEmpty(conversationId))
            {
                _logger.LogWarning("EmitStreamingTextUpdate called with null/empty conversationId");
                return;
            }
            if (string.IsNullOrEmpty(text) &&
                (!isComplete || (!inputTokens.HasValue && !outputTokens.HasValue)))
            {
                _logger.LogDebug("EmitStreamingTextUpdate called with null/empty text, skipping");
                return;
            }

            var message = new ConversationMessage("assistant", text)
            {
                IsStreaming = !isComplete,
                InputTokens = isComplete ? inputTokens : null,
                OutputTokens = isComplete ? outputTokens : null,
                Model = isComplete ? model : null
            };
            _publish(new ConversationUpdatedEvent
            {
                ConversationId = conversationId,
                Messages = [message],
                ToolCalls = [],
                ToolResults = []
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in EmitStreamingTextUpdate: {Message}", ex.Message);
            throw;
        }
    }

    public void EmitRetryNotification(string conversationId, int attempt, int maxRetries, int delaySeconds)
    {
        _publish(new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = [new ConversationMessage("assistant",
                $"⏳ Rate limit reached. Retrying in {delaySeconds}s (attempt {attempt}/{maxRetries})...")],
            ToolCalls = [],
            ToolResults = []
        });
    }

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
