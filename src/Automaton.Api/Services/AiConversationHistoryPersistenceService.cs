using Automaton.Ai.Models;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Automaton.Api.Services;

public sealed class AiConversationHistoryPersistenceService(
    IAiChatHistoryService chatHistoryService,
    ILogger<AiConversationHistoryPersistenceService> logger)
{
    public async Task<Guid> ResolveSessionAsync(
        Guid? sessionId,
        Guid? resourceId,
        ChatResourceType resourceType,
        Guid userId,
        string provider,
        string model)
    {
        if (sessionId.HasValue)
            return sessionId.Value;

        if (resourceId.HasValue)
        {
            var session = await chatHistoryService.GetOrCreateActiveSessionAsync(
                resourceType,
                resourceId.Value,
                userId,
                provider,
                model);
            logger.LogDebug("Auto-created session {SessionId} for resource {ResourceId}", session.Id, resourceId);
            return session.Id;
        }

        var generalSession = await chatHistoryService.CreateSessionAsync(new CreateChatSessionRequest
        {
            ResourceType = resourceType,
            ResourceId = Guid.Empty,
            Title = "New Chat",
            Provider = provider,
            Model = model
        }, userId);
        return generalSession.Id;
    }

    public async Task<List<ConversationMessage>?> LoadConversationHistoryAsync(Guid sessionId, Guid userId)
    {
        try
        {
            var messages = await chatHistoryService.GetMessagesAsync(sessionId, userId);
            return messages
                .Where(message => message.MessageType == ChatMessageType.Text)
                .Select(message => new ConversationMessage(message.Role, message.Content))
                .ToList();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load conversation history for session {SessionId}", sessionId);
            return null;
        }
    }

    public Task SaveUserMessageAsync(Guid sessionId, string content, Guid userId)
    {
        return chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
        {
            SessionId = sessionId,
            Role = "user",
            Content = content,
            MessageType = ChatMessageType.Text,
            Metadata = "{}"
        }, userId);
    }

    public Task IncrementApiCallsAsync(Guid sessionId, Guid userId, int apiCallCount)
    {
        return chatHistoryService.IncrementApiCallsAsync(sessionId, userId, apiCallCount);
    }

    public async Task SaveConversationUpdateAsync(
        ConversationUpdatedEvent update,
        Guid sessionId,
        Guid userId,
        HashSet<string> savedToolCallIds)
    {
        try
        {
            await SaveMessagesAsync(update.Messages, sessionId, userId);
            await SaveToolCallsAsync(update.ToolCalls, sessionId, userId, savedToolCallIds);
            await SaveToolResultsAsync(update.ToolResults, sessionId, userId, savedToolCallIds);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error saving conversation update for session {SessionId}", sessionId);
        }
    }

    public async Task FlushPendingTokenUsageAsync(
        IReadOnlyList<ConversationUpdatedEvent> updates,
        Guid sessionId,
        Guid userId)
    {
        if (updates.Count == 0)
            return;

        var messages = updates.SelectMany(update => update.Messages).ToList();
        if (!messages.Any(HasTokenUsage))
            return;

        var inputTokens = SumTokens(messages, message => message.InputTokens);
        var outputTokens = SumTokens(messages, message => message.OutputTokens);
        var cacheReadTokens = SumTokens(messages, message => message.CacheReadTokens);
        var cacheCreationTokens = SumTokens(messages, message => message.CacheCreationTokens);

        await chatHistoryService.AccumulateTokensAsync(
            sessionId,
            userId,
            inputTokens > 0 ? inputTokens : null,
            outputTokens > 0 ? outputTokens : null,
            cacheReadTokens > 0 ? cacheReadTokens : null,
            cacheCreationTokens > 0 ? cacheCreationTokens : null);
    }

    public async Task FlushPendingTokenUsageIfPossibleAsync(
        Guid? sessionId,
        Guid userId,
        ConcurrentQueue<ConversationUpdatedEvent> pendingUpdates)
    {
        if (!sessionId.HasValue || userId == Guid.Empty)
            return;

        var updatesSnapshot = pendingUpdates.ToArray();
        if (updatesSnapshot.Length == 0)
            return;

        await FlushPendingTokenUsageAsync(updatesSnapshot, sessionId.Value, userId);
    }

    public async Task EnsurePolicyMarkerMessagesAsync(
        Guid sessionId,
        Guid userId,
        string? verificationStrategy,
        string? executionPolicy)
    {
        var existingMessages = await chatHistoryService.GetMessagesAsync(sessionId, userId);
        await EnsureSinglePolicyMarkerMessageAsync(
            sessionId,
            userId,
            existingMessages,
            "Verification strategy: ",
            verificationStrategy);
        await EnsureSinglePolicyMarkerMessageAsync(
            sessionId,
            userId,
            existingMessages,
            "Execution policy: ",
            executionPolicy);
    }

    private async Task SaveMessagesAsync(
        IReadOnlyList<ConversationMessage> messages,
        Guid sessionId,
        Guid userId)
    {
        foreach (var message in messages)
        {
            if (message.IsStreaming)
                continue;

            if (string.IsNullOrEmpty(message.Content))
            {
                await AccumulateMessageTokensAsync(message, sessionId, userId);
                continue;
            }

            await chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
            {
                SessionId = sessionId,
                Role = message.Role,
                Content = message.Content,
                MessageType = ChatMessageType.Text,
                InputTokens = message.InputTokens,
                OutputTokens = message.OutputTokens,
                Model = message.Model,
                CacheReadTokens = message.CacheReadTokens,
                CacheCreationTokens = message.CacheCreationTokens,
                Metadata = "{}"
            }, userId);
        }
    }

    private Task AccumulateMessageTokensAsync(ConversationMessage message, Guid sessionId, Guid userId)
    {
        if (!HasTokenUsage(message))
            return Task.CompletedTask;

        return chatHistoryService.AccumulateTokensAsync(
            sessionId,
            userId,
            message.InputTokens,
            message.OutputTokens,
            message.CacheReadTokens,
            message.CacheCreationTokens);
    }

    private async Task SaveToolCallsAsync(
        IReadOnlyList<ToolCall> toolCalls,
        Guid sessionId,
        Guid userId,
        HashSet<string> savedToolCallIds)
    {
        foreach (var toolCall in toolCalls)
        {
            if (!savedToolCallIds.Add(toolCall.Id))
                continue;

            await chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
            {
                SessionId = sessionId,
                Role = "assistant",
                Content = $"Executing tool: {toolCall.Name}",
                MessageType = ChatMessageType.ToolExecution,
                Metadata = JsonSerializer.Serialize(new
                {
                    toolCallId = toolCall.Id,
                    toolName = toolCall.Name,
                    arguments = toolCall.Arguments
                })
            }, userId);
        }
    }

    private async Task SaveToolResultsAsync(
        IReadOnlyList<ToolResult> toolResults,
        Guid sessionId,
        Guid userId,
        HashSet<string> savedToolCallIds)
    {
        foreach (var toolResult in toolResults)
        {
            if (!savedToolCallIds.Add($"result:{toolResult.ToolCallId}"))
                continue;

            await chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
            {
                SessionId = sessionId,
                Role = "tool",
                Content = toolResult.Result ?? string.Empty,
                MessageType = toolResult.IsSuccess ? ChatMessageType.OperationComplete : ChatMessageType.CodeError,
                Metadata = JsonSerializer.Serialize(new
                {
                    toolCallId = toolResult.ToolCallId,
                    success = toolResult.IsSuccess,
                    error = toolResult.ErrorMessage
                })
            }, userId);
        }
    }

    private async Task EnsureSinglePolicyMarkerMessageAsync(
        Guid sessionId,
        Guid userId,
        IReadOnlyList<ChatMessageDto> existingMessages,
        string prefix,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var hasMarker = existingMessages.Any(message =>
            message.Role.Equals("system", StringComparison.OrdinalIgnoreCase) &&
            message.Content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (hasMarker)
            return;

        await chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
        {
            SessionId = sessionId,
            Role = "system",
            Content = $"{prefix}{value}",
            MessageType = ChatMessageType.System,
            Metadata = "{}"
        }, userId);
    }

    private static bool HasTokenUsage(ConversationMessage message)
    {
        return message.InputTokens.HasValue ||
               message.OutputTokens.HasValue ||
               message.CacheReadTokens.HasValue ||
               message.CacheCreationTokens.HasValue;
    }

    private static int SumTokens(
        IEnumerable<ConversationMessage> messages,
        Func<ConversationMessage, int?> selectTokens)
    {
        return messages.Aggregate(
            0,
            (total, message) => unchecked(total + (selectTokens(message) ?? 0)));
    }
}
