using Automaton.Mcp.Enums;
using Automaton.Mcp.Models;
using Automaton.Mcp.Services;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// MCP client implementation with common AI conversation logic.
/// Provides OpenAI integration and works with any IMcpToolProvider.
/// </summary>
public class BaseMcpClient : IMcpClient
{
    protected readonly Microsoft.Extensions.Logging.ILogger Logger;
    protected readonly IMcpToolProvider ToolProvider;
    private readonly ConcurrentDictionary<string, ConversationContext> _conversations = new();

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    public BaseMcpClient(ILogger<BaseMcpClient> logger, IMcpToolProvider toolProvider)
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
    /// Call OpenAI with MCP tools using the official OpenAI .NET SDK
    /// </summary>
    protected async Task<string> CallOpenAiAsync(ConversationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Use the official OpenAI .NET SDK pattern with tools
            var client = new ChatClient(model: request.Model ?? "gpt-4.1", apiKey: request.ApiKey);

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
                var completion = await client.CompleteChatAsync(messages, options, cancellationToken);

                switch (completion.Value.FinishReason)
                {
                    case ChatFinishReason.Stop:
                        // Extract assistant's text response
                        var assistantText = completion.Value.Content[0].Text ?? "";

                        // Add the assistant message to conversation history
                        messages.Add(ChatMessage.CreateAssistantMessage(assistantText));
                        responseBuilder.AppendLine(assistantText);

                        // Emit AI reasoning message if it contains text
                        if (!string.IsNullOrWhiteSpace(assistantText))
                        {
                            var reasoningMessage = new ConversationMessage("assistant", assistantText);
                            allConversationMessages.Add(reasoningMessage);

                            ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
                            {
                                ConversationId = request.ConversationId,
                                Messages = new List<ConversationMessage> { reasoningMessage },
                                ToolCalls = new List<ToolCall>(),
                                ToolResults = new List<ToolResult>()
                            });
                        }
                        break;

                    case ChatFinishReason.ToolCalls:
                        // Check if there's text content alongside tool calls (AI reasoning/thinking)
                        var thinkingText = completion.Value.Content
                            .Where(c => c.Kind == ChatMessageContentPartKind.Text)
                            .Select(c => c.Text)
                            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

                        // Emit AI reasoning/thinking message if present
                        if (!string.IsNullOrWhiteSpace(thinkingText))
                        {
                            var thinkingMessage = new ConversationMessage("assistant", thinkingText);
                            allConversationMessages.Add(thinkingMessage);

                            ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
                            {
                                ConversationId = request.ConversationId,
                                Messages = new List<ConversationMessage> { thinkingMessage },
                                ToolCalls = new List<ToolCall>(),
                                ToolResults = new List<ToolResult>()
                            });

                            Logger.LogInformation("AI reasoning: {Thinking}", thinkingText);
                        }

                        // First, add the assistant message with tool calls to conversation history
                        messages.Add(ChatMessage.CreateAssistantMessage(completion.Value));

                        // Then, execute each tool call and add results
                        foreach (var toolCall in completion.Value.ToolCalls)
                        {
                            // Emit tool call started event
                            var currentToolCalls = new List<ToolCall>
                            {
                                new ToolCall
                                {
                                    Id = toolCall.Id,
                                    Name = toolCall.FunctionName,
                                    Arguments = JsonDocument.Parse(toolCall.FunctionArguments?.ToString() ?? "{}").RootElement.EnumerateObject()
                                        .ToDictionary(p => p.Name, p => (object?)p.Value.ToString())
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
                            var toolResult = await ExecuteMcpToolAsync(toolCall, request);

                            // Create tool result
                            var toolResultObj = new ToolResult
                            {
                                ToolCallId = toolCall.Id,
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
                            messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, toolResult));
                        }

                        requiresAction = true;
                        break;

                    case ChatFinishReason.Length:
                        var lengthErrorMsg = "Incomplete model output due to token limit";

                        // Emit error as conversation update so UI can display it
                        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
                        {
                            ConversationId = request.ConversationId,
                            Messages = new List<ConversationMessage>
                            {
                                new ConversationMessage("assistant", $"Error: {lengthErrorMsg}")
                            },
                            ToolCalls = new List<ToolCall>(),
                            ToolResults = new List<ToolResult>()
                        });

                        throw new InvalidOperationException(lengthErrorMsg);

                    default:
                        var unexpectedErrorMsg = $"Unexpected finish reason: {completion.Value.FinishReason}";

                        // Emit error as conversation update so UI can display it
                        ConversationUpdated?.Invoke(this, new ConversationUpdatedEvent
                        {
                            ConversationId = request.ConversationId,
                            Messages = new List<ConversationMessage>
                            {
                                new ConversationMessage("assistant", $"Error: {unexpectedErrorMsg}")
                            },
                            ToolCalls = new List<ToolCall>(),
                            ToolResults = new List<ToolResult>()
                        });

                        throw new InvalidOperationException(unexpectedErrorMsg);
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
    private async Task<string> ExecuteMcpToolAsync(ChatToolCall toolCall, ConversationRequest request)
    {
        try
        {
            var arguments = JsonDocument.Parse(toolCall.FunctionArguments?.ToString() ?? "{}");

            // Extract context ID from request - could be stepId, appId, or any other context identifier
            var contextId = request.Context?.GetValueOrDefault("contextId")?.ToString();

            // Pass the full context dictionary to the tool provider so it can access context-specific functions
            var result = await ToolProvider.ExecuteToolAsync(toolCall.FunctionName, arguments.RootElement, contextId, request.Context);

            if (result.Success)
            {
                return result.Content ?? "Tool executed successfully";
            }
            else
            {
                // Include both the error and the content (which contains the full error details in JSON)
                var errorMessage = $"Tool execution failed: {result.Error}";

                // If there's content with detailed error info, include it
                if (!string.IsNullOrEmpty(result.Content))
                {
                    errorMessage += $"\n\nDetailed Result:\n{result.Content}";
                }

                return errorMessage;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing MCP tool {ToolName}", toolCall.FunctionName);
            return $"Error executing tool {toolCall.FunctionName}: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
        }
    }
}
