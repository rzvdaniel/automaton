using Automaton.Mcp.Enums;
using Automaton.Mcp.Models;
using Automaton.Mcp.Services;
using Automaton.Studio.Services.Mcp.Clients.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Automaton.Studio.Services.Mcp.Clients;

/// <summary>
/// MCP client implementation with common AI conversation logic.
/// Uses provider abstraction for AI integrations (OpenAI, Anthropic, etc.)
/// </summary>
public class McpClient : IMcpClient
{
    protected readonly Microsoft.Extensions.Logging.ILogger Logger;
    protected readonly IMcpToolProvider ToolProvider;
    private readonly ConcurrentDictionary<string, ConversationContext> _conversations = new();
    private readonly Dictionary<AiProviderType, IAiProvider> _providers = [];

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    public McpClient(ILogger<McpClient> logger, IMcpToolProvider toolProvider)
    {
        Logger = logger;
        ToolProvider = toolProvider;

        InitializeProviders();
    }

    private void InitializeProviders()
    {
        // Create OpenAI provider
        var openAiProvider = new OpenAiProvider(Logger);
        openAiProvider.ConversationUpdated += OnProviderConversationUpdated;
        _providers[AiProviderType.OpenAI] = openAiProvider;

        // Create Anthropic provider
        var anthropicProvider = new AnthropicProvider(Logger);
        anthropicProvider.ConversationUpdated += OnProviderConversationUpdated;
        _providers[AiProviderType.Anthropic] = anthropicProvider;
    }

    private void OnProviderConversationUpdated(object? sender, ConversationUpdatedEvent e)
    {
        // Forward provider events to McpClient subscribers
        ConversationUpdated?.Invoke(this, e);
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

            // Check if we have a provider for the requested type
            if (_providers.TryGetValue(request.Provider, out var provider))
            {
                // Get tools from the tool provider
                var providerTools = await CreateProviderToolsAsync();

                // Call the provider
                responseText = await provider.CallAsync(
                    request,
                    providerTools,
                    async (toolCallId, toolName, argsJson) => await ExecuteMcpToolAsync(toolCallId, toolName, argsJson, request),
                    cancellationToken);
            }
            else
            {
                Logger.LogWarning("Provider {Provider} not supported", request.Provider);
                responseText = $"Provider {request.Provider} is not yet supported. Please use OpenAI or Anthropic.";
            }

            // Add conversation messages
            conversationHistory.Add(new ConversationMessage("user", request.InitialMessage));
            conversationHistory.Add(new ConversationMessage("assistant", responseText));

            // NOTE: Do NOT emit ConversationUpdated event here - messages are already emitted
            // in real-time during the conversation by the provider.
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
    /// Create provider tools from the MCP tool provider
    /// </summary>
    private async Task<List<ProviderTool>> CreateProviderToolsAsync()
    {
        var mcpTools = await ToolProvider.GetToolsAsync();
        var providerTools = new List<ProviderTool>();

        foreach (var tool in mcpTools)
        {
            providerTools.Add(new ProviderTool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchemaJson = tool.InputSchema?.RootElement.GetRawText() ?? "{}"
            });
        }

        return providerTools;
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
}
