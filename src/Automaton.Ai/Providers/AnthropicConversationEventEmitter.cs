using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Automaton.Ai.Providers;

internal sealed class AnthropicConversationEventEmitter
{
    private readonly ILogger _logger;
    private readonly Action<ConversationUpdatedEvent> _publish;

    public AnthropicConversationEventEmitter(
        ILogger logger,
        Action<ConversationUpdatedEvent> publish)
    {
        _logger = logger;
        _publish = publish;
    }

    public void EmitRetryNotification(string conversationId, int attempt, int maxRetries, int delaySeconds)
    {
        Publish(conversationId, new ConversationMessage("assistant",
            $"⏳ Rate limit reached. Retrying in {delaySeconds}s (attempt {attempt}/{maxRetries})..."));
    }

    public void EmitToolCallStarted(string conversationId, string toolCallId, string toolName, string argsJson)
    {
        Publish(conversationId, toolCall: new ToolCall
        {
            Id = toolCallId,
            Name = toolName,
            Arguments = ParseToolArguments(argsJson)
        });
    }

    public void EmitToolCallCompleted(
        string conversationId,
        string toolCallId,
        string toolName,
        string argsJson,
        string result)
    {
        var toolCall = new ToolCall
        {
            Id = toolCallId,
            Name = toolName,
            Arguments = ParseToolArguments(argsJson)
        };
        var toolResult = new ToolResult
        {
            ToolCallId = toolCallId,
            Result = result,
            IsSuccess = !IsToolFailure(result),
            ErrorMessage = IsToolFailure(result) ? result : null
        };

        Publish(conversationId, toolCall: toolCall, toolResult: toolResult);
    }

    public void EmitStreamingTextUpdate(
        string conversationId,
        string text,
        bool isComplete,
        int? inputTokens = null,
        int? outputTokens = null,
        string? model = null,
        int? cacheReadTokens = null,
        int? cacheCreationTokens = null)
    {
        try
        {
            if (string.IsNullOrEmpty(conversationId))
            {
                _logger.LogWarning("EmitStreamingTextUpdate called with null/empty conversationId");
                return;
            }

            if (ShouldSkipStreamingText(text, isComplete, inputTokens, outputTokens, cacheReadTokens, cacheCreationTokens))
            {
                _logger.LogDebug("EmitStreamingTextUpdate called with null/empty text, skipping");
                return;
            }

            var message = new ConversationMessage("assistant", text)
            {
                IsStreaming = !isComplete,
                InputTokens = isComplete ? inputTokens : null,
                OutputTokens = isComplete ? outputTokens : null,
                Model = isComplete ? model : null,
                CacheReadTokens = isComplete ? cacheReadTokens : null,
                CacheCreationTokens = isComplete ? cacheCreationTokens : null,
            };
            Publish(conversationId, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in EmitStreamingTextUpdate: {Message}", ex.Message);
            throw;
        }
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
            Messages = message is null ? new List<ConversationMessage>() : new List<ConversationMessage> { message },
            ToolCalls = toolCall is null ? new List<ToolCall>() : new List<ToolCall> { toolCall },
            ToolResults = toolResult is null ? new List<ToolResult>() : new List<ToolResult> { toolResult }
        });
    }

    private static bool ShouldSkipStreamingText(
        string text,
        bool isComplete,
        int? inputTokens,
        int? outputTokens,
        int? cacheReadTokens,
        int? cacheCreationTokens) =>
        string.IsNullOrEmpty(text) &&
        (!isComplete || (!inputTokens.HasValue && !outputTokens.HasValue &&
                         !cacheReadTokens.HasValue && !cacheCreationTokens.HasValue));

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
