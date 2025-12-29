using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.Text;
using System.Text.Json;

namespace Automaton.Studio.Services.Mcp.Clients.Providers;

/// <summary>
/// OpenAI provider implementation using the official OpenAI .NET SDK
/// </summary>
public class OpenAiProvider : IAiProvider
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger;

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    public OpenAiProvider(Microsoft.Extensions.Logging.ILogger logger)
    {
        _logger = logger;
    }

    public async Task<string> CallAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = new ChatClient(model: request.Model, apiKey: request.ApiKey);

            // Build conversation messages
            var messages = new List<ChatMessage>();

            // Add system prompt if provided
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                messages.Add(ChatMessage.CreateSystemMessage(request.SystemPrompt));
            }

            // Add conversation history
            if (request.ConversationHistory?.Any() == true)
            {
                foreach (var msg in request.ConversationHistory)
                {
                    ChatMessage chatMessage = msg.Role.ToLowerInvariant() switch
                    {
                        "user" => ChatMessage.CreateUserMessage(msg.Content),
                        "assistant" => ChatMessage.CreateAssistantMessage(msg.Content),
                        _ => ChatMessage.CreateUserMessage(msg.Content)
                    };
                    messages.Add(chatMessage);
                }
            }

            // Add current user message
            messages.Add(ChatMessage.CreateUserMessage(request.InitialMessage));

            // Create chat completion options with tools
            var options = new ChatCompletionOptions();
            foreach (var tool in tools)
            {
                options.Tools.Add(ChatTool.CreateFunctionTool(
                    functionName: tool.Name,
                    functionDescription: tool.Description,
                    functionParameters: BinaryData.FromString(tool.InputSchemaJson)
                ));
            }

            // Handle iterative conversation with tool calls
            bool requiresAction;
            var responseBuilder = new StringBuilder();
            var allConversationMessages = new List<ConversationMessage>();

            do
            {
                requiresAction = false;

                // Use streaming for text responses
                var streamingResult = await StreamResponseAsync(
                    client, messages, options, request.ConversationId,
                    responseBuilder, allConversationMessages, cancellationToken);

                requiresAction = streamingResult.RequiresAction;

                if (streamingResult.RequiresAction && streamingResult.ToolCalls.Any())
                {
                    // Add the assistant message with tool calls to conversation history
                    var toolCallsList = CreateToolCallsList(streamingResult.ToolCalls);
                    var assistantMessage = ChatMessage.CreateAssistantMessage(toolCallsList);
                    messages.Add(assistantMessage);

                    // Execute each tool call
                    foreach (var toolCallInfo in streamingResult.ToolCalls)
                    {
                        var argsJson = string.IsNullOrEmpty(toolCallInfo.ArgumentsJson) ? "{}" : toolCallInfo.ArgumentsJson;

                        // Emit tool call started event
                        EmitToolCallStarted(request.ConversationId, toolCallInfo.Id, toolCallInfo.Name, argsJson);

                        // Execute the tool
                        var toolResult = await executeToolAsync(toolCallInfo.Id, toolCallInfo.Name, argsJson);

                        // Emit tool execution completed
                        EmitToolCallCompleted(request.ConversationId, toolCallInfo.Id, toolCallInfo.Name, argsJson, toolResult);

                        // Add tool message to conversation
                        messages.Add(ChatMessage.CreateToolMessage(toolCallInfo.Id, toolResult));
                    }
                }

            } while (requiresAction);

            return responseBuilder.ToString().Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI API with MCP tools");

            // Emit error as conversation update
            ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
            {
                ConversationId = request.ConversationId,
                Messages = new List<ConversationMessage>
                {
                    new ConversationMessage("assistant", $"Error: {ex.Message}")
                },
                ToolCalls = new List<ToolCall>(),
                ToolResults = new List<ToolResult>()
            });

            throw;
        }
    }

    private List<ChatToolCall> CreateToolCallsList(List<ToolCallInfo> toolCalls)
    {
        var toolCallsList = new List<ChatToolCall>();
        foreach (var tc in toolCalls)
        {
            var argsData = string.IsNullOrEmpty(tc.ArgumentsJson) ? "{}" : tc.ArgumentsJson;
            _logger.LogDebug("Creating tool call: Id={Id}, Name={Name}, Args={Args}", tc.Id, tc.Name, argsData);

            var binaryArgs = BinaryData.FromString(argsData);
            var toolCall = ChatToolCall.CreateFunctionToolCall(tc.Id, tc.Name, binaryArgs);
            toolCallsList.Add(toolCall);
        }
        return toolCallsList;
    }

    private void EmitToolCallStarted(string conversationId, string toolCallId, string toolName, string argsJson)
    {
        var currentToolCalls = new List<ToolCall>
        {
            new ToolCall
            {
                Id = toolCallId,
                Name = toolName,
                Arguments = ParseToolArguments(argsJson)
            }
        };

        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = new List<ConversationMessage>(),
            ToolCalls = currentToolCalls,
            ToolResults = new List<ToolResult>()
        });
    }

    private void EmitToolCallCompleted(string conversationId, string toolCallId, string toolName, string argsJson, string result)
    {
        var currentToolCalls = new List<ToolCall>
        {
            new ToolCall
            {
                Id = toolCallId,
                Name = toolName,
                Arguments = ParseToolArguments(argsJson)
            }
        };

        var toolResultObj = new ToolResult
        {
            ToolCallId = toolCallId,
            Result = result,
            IsSuccess = !result.StartsWith("Tool execution failed:") && !result.StartsWith("Error executing tool"),
            ErrorMessage = result.StartsWith("Tool execution failed:") || result.StartsWith("Error executing tool") ? result : null
        };

        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
        {
            ConversationId = conversationId,
            Messages = new List<ConversationMessage>(),
            ToolCalls = currentToolCalls,
            ToolResults = new List<ToolResult> { toolResultObj }
        });
    }

    private Dictionary<string, object?> ParseToolArguments(string argsJson)
    {
        try
        {
            if (string.IsNullOrEmpty(argsJson) || argsJson == "{}")
                return new Dictionary<string, object?>();

            return JsonDocument.Parse(argsJson).RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => (object?)p.Value.ToString());
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private async Task<StreamingResult> StreamResponseAsync(
        ChatClient client,
        List<ChatMessage> messages,
        ChatCompletionOptions options,
        string conversationId,
        StringBuilder responseBuilder,
        List<ConversationMessage> allConversationMessages,
        CancellationToken cancellationToken)
    {
        var result = new StreamingResult();
        var currentText = new StringBuilder();
        var toolCallsById = new Dictionary<int, StreamingToolCallInfo>();
        ChatFinishReason? finishReason = null;

        // Track when we last emitted an update for throttling
        var lastEmitTime = DateTime.UtcNow;
        var emitIntervalMs = 100;

        try
        {
            await foreach (var update in client.CompleteChatStreamingAsync(messages, options, cancellationToken))
            {
                // Process content updates (streaming text)
                if (update.ContentUpdate != null && update.ContentUpdate.Count > 0)
                {
                    foreach (var contentPart in update.ContentUpdate)
                    {
                        if (contentPart != null && !string.IsNullOrEmpty(contentPart.Text))
                        {
                            currentText.Append(contentPart.Text);

                            // Throttle updates to avoid overwhelming the UI
                            var now = DateTime.UtcNow;
                            if ((now - lastEmitTime).TotalMilliseconds >= emitIntervalMs)
                            {
                                EmitStreamingTextUpdate(conversationId, currentText.ToString(), isComplete: false);
                                lastEmitTime = now;
                            }
                        }
                    }
                }

                // Process tool call updates
                if (update.ToolCallUpdates != null && update.ToolCallUpdates.Count > 0)
                {
                    foreach (var toolCallUpdate in update.ToolCallUpdates)
                    {
                        if (toolCallUpdate == null) continue;

                        if (!toolCallsById.TryGetValue(toolCallUpdate.Index, out var toolInfo))
                        {
                            toolInfo = new StreamingToolCallInfo
                            {
                                Index = toolCallUpdate.Index,
                                Id = toolCallUpdate.ToolCallId ?? "",
                                Name = toolCallUpdate.FunctionName ?? "",
                                ArgumentsBuilder = new StringBuilder()
                            };
                            toolCallsById[toolCallUpdate.Index] = toolInfo;
                        }

                        if (!string.IsNullOrEmpty(toolCallUpdate.ToolCallId))
                            toolInfo.Id = toolCallUpdate.ToolCallId;
                        if (!string.IsNullOrEmpty(toolCallUpdate.FunctionName))
                            toolInfo.Name = toolCallUpdate.FunctionName;

                        // Safely extract function arguments
                        if (toolCallUpdate.FunctionArgumentsUpdate != null)
                        {
                            try
                            {
                                var binaryData = toolCallUpdate.FunctionArgumentsUpdate;
                                if (binaryData.ToMemory().Length > 0)
                                {
                                    var argsUpdate = binaryData.ToString();
                                    if (!string.IsNullOrEmpty(argsUpdate))
                                        toolInfo.ArgumentsBuilder.Append(argsUpdate);
                                }
                            }
                            catch (ArgumentNullException)
                            {
                                _logger.LogDebug("Skipping tool call arguments update with null bytes");
                            }
                        }
                    }
                }

                // Capture finish reason
                if (update.FinishReason.HasValue)
                {
                    finishReason = update.FinishReason.Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OpenAI streaming: {Message}", ex.Message);
            throw;
        }

        // Handle final state based on finish reason
        if (finishReason == ChatFinishReason.Stop)
        {
            var finalText = currentText.ToString();
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                EmitStreamingTextUpdate(conversationId, finalText, isComplete: true);
                responseBuilder.AppendLine(finalText);
                messages.Add(ChatMessage.CreateAssistantMessage(finalText));
                allConversationMessages.Add(new ConversationMessage("assistant", finalText));
            }
            result.RequiresAction = false;
        }
        else if (finishReason == ChatFinishReason.ToolCalls)
        {
            var thinkingText = currentText.ToString();
            if (!string.IsNullOrWhiteSpace(thinkingText))
            {
                EmitStreamingTextUpdate(conversationId, thinkingText, isComplete: true);
                allConversationMessages.Add(new ConversationMessage("assistant", thinkingText));
                _logger.LogInformation("AI reasoning: {Thinking}", thinkingText);
            }

            foreach (var toolInfo in toolCallsById.Values.OrderBy(t => t.Index))
            {
                var argsString = toolInfo.ArgumentsBuilder.ToString();
                result.ToolCalls.Add(new ToolCallInfo
                {
                    Id = toolInfo.Id,
                    Name = toolInfo.Name,
                    ArgumentsJson = string.IsNullOrEmpty(argsString) ? "{}" : argsString
                });
            }
            result.RequiresAction = true;
        }
        else if (finishReason == ChatFinishReason.Length)
        {
            ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
            {
                ConversationId = conversationId,
                Messages = new List<ConversationMessage>
                {
                    new ConversationMessage("assistant", "Error: Response truncated due to token limit")
                },
                ToolCalls = new List<ToolCall>(),
                ToolResults = new List<ToolResult>()
            });
            throw new InvalidOperationException("Incomplete model output due to token limit");
        }

        return result;
    }

    private void EmitStreamingTextUpdate(string conversationId, string text, bool isComplete)
    {
        try
        {
            if (string.IsNullOrEmpty(conversationId))
            {
                _logger.LogWarning("EmitStreamingTextUpdate called with null/empty conversationId");
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                _logger.LogDebug("EmitStreamingTextUpdate called with null/empty text, skipping");
                return;
            }

            var message = new ConversationMessage("assistant", text)
            {
                IsStreaming = !isComplete
            };

            ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
            {
                ConversationId = conversationId,
                Messages = new List<ConversationMessage> { message },
                ToolCalls = new List<ToolCall>(),
                ToolResults = new List<ToolResult>()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in EmitStreamingTextUpdate: {Message}", ex.Message);
            throw;
        }
    }

    // Helper classes for streaming
    private class StreamingResult
    {
        public bool RequiresAction { get; set; }
        public List<ToolCallInfo> ToolCalls { get; } = new();
    }

    private class ToolCallInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ArgumentsJson { get; set; } = "{}";
    }

    private class StreamingToolCallInfo
    {
        public int Index { get; set; }
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public StringBuilder ArgumentsBuilder { get; set; } = new();
    }
}
