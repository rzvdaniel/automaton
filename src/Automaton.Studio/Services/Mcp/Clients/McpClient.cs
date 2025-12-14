using Automaton.Mcp.Enums;
using Automaton.Mcp.Models;
using Automaton.Mcp.Services;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Automaton.Studio.Services.Mcp.Clients;

/// <summary>
/// MCP client implementation with common AI conversation logic.
/// Provides OpenAI integration and works with any IMcpToolProvider.
/// </summary>
public class McpClient : IMcpClient
{
    protected readonly Microsoft.Extensions.Logging.ILogger Logger;
    protected readonly IMcpToolProvider ToolProvider;
    private readonly ConcurrentDictionary<string, ConversationContext> _conversations = new();

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    public McpClient(ILogger<McpClient> logger, IMcpToolProvider toolProvider)
    {
        Logger = logger;
        ToolProvider = toolProvider;
    }

    public async Task<ConversationResult> StartConversationAsync(ConversationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInformation("Starting conversation {ConversationId} with provider {Provider}",
                request.ConversationId, request.Provider);

            // Store conversation context with request context
            _conversations[request.ConversationId] = new ConversationContext
            {
                ConversationId = request.ConversationId,
                StartTime = DateTime.UtcNow,
                Metadata = request.Context ?? new Dictionary<string, object>()
            };

            // Start conversation using direct AI provider SDKs
            return await HandleDirectAiConversation(request, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error starting conversation {ConversationId}", request.ConversationId);
            return new ConversationResult
            {
                ConversationId = request.ConversationId,
                IsSuccess = false,
                ErrorMessage = $"Failed to start conversation: {ex.Message}"
            };
        }
    }

    public Task<ConversationResult> ContinueConversationAsync(string conversationId, string message, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Continuing conversation {ConversationId}", conversationId);

        if (!_conversations.ContainsKey(conversationId))
        {
            return Task.FromResult(new ConversationResult
            {
                ConversationId = conversationId,
                IsSuccess = false,
                ErrorMessage = $"Conversation {conversationId} not found"
            });
        }

        // For now, treat continue as not implemented
        return Task.FromResult(new ConversationResult
        {
            ConversationId = conversationId,
            IsSuccess = false,
            ErrorMessage = "Continue conversation not yet implemented"
        });
    }

    public Task<bool> EndConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInformation("Ending conversation {ConversationId}", conversationId);
            _conversations.TryRemove(conversationId, out _);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error ending conversation {ConversationId}", conversationId);
            return Task.FromResult(false);
        }
    }

    public void Dispose()
    {
        // Clean up resources
        _conversations.Clear();
    }

    private async Task<ConversationResult> HandleDirectAiConversation(
        ConversationRequest request,
        CancellationToken cancellationToken)
    {
        var conversationHistory = new List<ConversationMessage>();
        var allToolCalls = new List<ToolCall>();
        var allToolResults = new List<ToolResult>();

        try
        {
            Logger.LogInformation("Calling AI provider {Provider} with model {Model}", request.Provider, request.Model);

            string responseText;

            switch (request.Provider)
            {
                case AiProviderType.OpenAI:
                    responseText = await CallOpenAiAsync(request, cancellationToken);
                    break;

                case AiProviderType.Anthropic:
                    // TODO: Implement Anthropic SDK integration
                    Logger.LogWarning("Anthropic provider not yet implemented, returning error message");
                    responseText = "Anthropic provider is not yet supported. Please use OpenAI or add your Anthropic implementation.";
                    break;

                default:
                    Logger.LogWarning("Provider {Provider} not supported", request.Provider);
                    responseText = $"Provider {request.Provider} is not yet supported. Please use OpenAI.";
                    break;
            }

            // Add conversation messages
            conversationHistory.Add(new ConversationMessage("user", request.InitialMessage));
            conversationHistory.Add(new ConversationMessage("assistant", responseText));

            // NOTE: Do NOT emit ConversationUpdated event here - messages are already emitted
            // in real-time during the conversation inside CallOpenAiAsync (for OpenAI) or
            // will be processed by the UI from the returned ConversationResult.
            // Emitting here would cause duplicate messages in the UI.

            return new ConversationResult
            {
                ConversationId = request.ConversationId,
                Messages = conversationHistory,
                ToolCalls = allToolCalls,
                ToolResults = allToolResults,
                IsSuccess = true,
                GeneratedCode = ExtractCodeFromMessages(conversationHistory)
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in direct AI conversation");

            return new ConversationResult
            {
                ConversationId = request.ConversationId,
                Messages = conversationHistory,
                ToolCalls = allToolCalls,
                ToolResults = allToolResults,
                IsSuccess = false,
                ErrorMessage = $"AI conversation failed: {ex.Message}"
            };
        }
    }

    private string ExtractCodeFromMessages(List<ConversationMessage> messages)
    {
        var lastAssistantMessage = messages.LastOrDefault(m => m.Role.ToLowerInvariant() == "assistant");
        return lastAssistantMessage?.Content ?? string.Empty;
    }

    /// <summary>
    /// Call OpenAI with MCP tools using the official OpenAI .NET SDK with streaming
    /// </summary>
    protected async Task<string> CallOpenAiAsync(ConversationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Use the official OpenAI .NET SDK pattern with tools
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

            // Create MCP tools for AI to use from integrated tool provider
            var tools = await CreateMcpToolsAsync();

            // Create chat completion options with tools
            var options = new ChatCompletionOptions();

            // Add tools to options
            foreach (var tool in tools)
            {
                options.Tools.Add(tool);
            }

            // Handle iterative conversation with tool calls
            bool requiresAction;
            var responseBuilder = new StringBuilder();
            var allConversationMessages = new List<ConversationMessage>();

            do
            {
                requiresAction = false;
                
                // Use streaming for text responses
                var streamingResult = await StreamOpenAiResponseAsync(
                    client, messages, options, request.ConversationId, 
                    responseBuilder, allConversationMessages, cancellationToken);
                
                requiresAction = streamingResult.RequiresAction;
                
                if (streamingResult.RequiresAction && streamingResult.ToolCalls.Any())
                {
                    try
                    {
                        // Add the assistant message with tool calls to conversation history
                        var toolCallsList = new List<ChatToolCall>();
                        foreach (var tc in streamingResult.ToolCalls)
                        {
                            var argsData = string.IsNullOrEmpty(tc.ArgumentsJson) ? "{}" : tc.ArgumentsJson;
                            Logger.LogDebug("Creating tool call: Id={Id}, Name={Name}, Args={Args}", tc.Id, tc.Name, argsData);
                            
                            // BREAKPOINT HERE: Check if argsData is valid before calling BinaryData.FromString
                            var binaryArgs = BinaryData.FromString(argsData);
                            var toolCall = ChatToolCall.CreateFunctionToolCall(tc.Id, tc.Name, binaryArgs);
                            toolCallsList.Add(toolCall);
                        }
                        
                        var assistantMessage = ChatMessage.CreateAssistantMessage(toolCallsList);
                        messages.Add(assistantMessage);
                    }
                    catch (Exception ex)
                    {
                        // BREAKPOINT HERE: Catch error during tool call list creation
                        Logger.LogError(ex, "Error creating tool call list: {Message}", ex.Message);
                        throw;
                    }

                    // Execute each tool call
                    foreach (var toolCallInfo in streamingResult.ToolCalls)
                    {
                        var argsJson = string.IsNullOrEmpty(toolCallInfo.ArgumentsJson) ? "{}" : toolCallInfo.ArgumentsJson;
                        
                        // Emit tool call started event
                        var currentToolCalls = new List<ToolCall>
                        {
                            new ToolCall
                            {
                                Id = toolCallInfo.Id,
                                Name = toolCallInfo.Name,
                                Arguments = ParseToolArguments(argsJson)
                            }
                        };

                        // Emit tool execution started
                        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
                        {
                            ConversationId = request.ConversationId,
                            Messages = new List<ConversationMessage>(),
                            ToolCalls = currentToolCalls,
                            ToolResults = new List<ToolResult>()
                        });

                        // Execute the tool
                        var toolResult = await ExecuteMcpToolAsync(toolCallInfo.Id, toolCallInfo.Name, argsJson, request);

                        // Create tool result
                        var toolResultObj = new ToolResult
                        {
                            ToolCallId = toolCallInfo.Id,
                            Result = toolResult,
                            IsSuccess = !toolResult.StartsWith("Tool execution failed:") && !toolResult.StartsWith("Error executing tool"),
                            ErrorMessage = toolResult.StartsWith("Tool execution failed:") || toolResult.StartsWith("Error executing tool") ? toolResult : null
                        };

                        // Emit tool execution completed
                        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
                        {
                            ConversationId = request.ConversationId,
                            Messages = new List<ConversationMessage>(),
                            ToolCalls = currentToolCalls,
                            ToolResults = new List<ToolResult> { toolResultObj }
                        });

                        // Add tool message to conversation
                        messages.Add(ChatMessage.CreateToolMessage(toolCallInfo.Id, toolResult));
                    }
                }

            } while (requiresAction);

            return responseBuilder.ToString().Trim();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error calling OpenAI API with MCP tools");

            // Emit error as conversation update so UI can display it
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

    /// <summary>
    /// Parse tool arguments JSON into a dictionary, with error handling
    /// </summary>
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

    /// <summary>
    /// Stream OpenAI response and emit real-time updates
    /// </summary>
    private async Task<StreamingResult> StreamOpenAiResponseAsync(
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
        var emitIntervalMs = 100; // Emit updates every 100ms

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
                        
                        // Safely extract function arguments - BinaryData.ToString() can throw if bytes are null
                        if (toolCallUpdate.FunctionArgumentsUpdate != null)
                        {
                            try
                            {
                                // Check if the BinaryData has content before calling ToString()
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
                                // BinaryData has null bytes - skip this update
                                Logger.LogDebug("Skipping tool call arguments update with null bytes");
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
            Logger.LogError(ex, "Error during OpenAI streaming: {Message}", ex.Message);
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
                Logger.LogInformation("AI reasoning: {Thinking}", thinkingText);
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

    /// <summary>
    /// Emit a streaming text update to the UI
    /// </summary>
    private void EmitStreamingTextUpdate(string conversationId, string text, bool isComplete)
    {
        try
        {
            // Validate inputs to prevent null reference errors
            if (string.IsNullOrEmpty(conversationId))
            {
                Logger.LogWarning("EmitStreamingTextUpdate called with null/empty conversationId");
                return;
            }
            
            if (string.IsNullOrEmpty(text))
            {
                Logger.LogDebug("EmitStreamingTextUpdate called with null/empty text, skipping");
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
            // BREAKPOINT HERE: This is where the error might be occurring
            Logger.LogError(ex, "Error in EmitStreamingTextUpdate: {Message}. ConversationId={ConversationId}, TextLength={TextLength}, IsComplete={IsComplete}", 
                ex.Message, conversationId, text?.Length ?? 0, isComplete);
            throw; // Re-throw to see full stack trace
        }
    }

    /// <summary>
    /// Create MCP tools that AI can use from the integrated tool provider
    /// </summary>
    private async Task<List<ChatTool>> CreateMcpToolsAsync()
    {
        var tools = await ToolProvider.GetToolsAsync();
        var chatTools = new List<ChatTool>();

        foreach (var tool in tools)
        {
            var schemaBytes = tool.InputSchema?.RootElement.GetRawText() ?? "{}";
            chatTools.Add(ChatTool.CreateFunctionTool(
                functionName: tool.Name,
                functionDescription: tool.Description,
                functionParameters: BinaryData.FromString(schemaBytes)
            ));
        }

        return chatTools;
    }

    /// <summary>
    /// Execute an MCP tool call using the integrated tool provider
    /// </summary>
    private async Task<string> ExecuteMcpToolAsync(string toolCallId, string toolName, string argumentsJson, ConversationRequest request)
    {
        try
        {
            var arguments = JsonDocument.Parse(string.IsNullOrEmpty(argumentsJson) ? "{}" : argumentsJson);

            var contextId = request.Context?.GetValueOrDefault("contextId")?.ToString();
            var result = await ToolProvider.ExecuteToolAsync(toolName, arguments.RootElement, contextId, request.Context);

            if (result.Success)
            {
                return result.Content ?? "Tool executed successfully";
            }
            else
            {
                var errorMessage = $"Tool execution failed: {result.Error}";
                if (!string.IsNullOrEmpty(result.Content))
                {
                    errorMessage += $"\n\nDetailed Result:\n{result.Content}";
                }
                return errorMessage;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing MCP tool {ToolName}", toolName);
            return $"Error executing tool {toolName}: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
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
