using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Models;
using Automaton.Mcp.Modes;
using Automaton.Mcp.Services;
using Automaton.Mcp.Client.Providers;
using OpenAI.Chat;

namespace Automaton.Mcp.Client;

/// <summary>
/// Simplified MCP client implementation using official AI provider SDKs and integrated tool provider
/// </summary>
public class AutomatonMcpClient : IAutomatonMcpClient, IDisposable
{
    private readonly ILogger<AutomatonMcpClient> _logger;
    private IMcpToolProvider? _toolProvider;
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly ConcurrentDictionary<string, ConversationContext> _conversations = new();
    
    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    public AutomatonMcpClient(
        ILogger<AutomatonMcpClient> logger,
        IAiProviderRegistry providerRegistry,
        IMcpToolProvider? toolProvider = null)
    {
        _logger = logger;
        _providerRegistry = providerRegistry;
        _toolProvider = toolProvider;
        
        // Subscribe to events from all registered providers
        foreach (var provider in _providerRegistry.Providers.Values)
        {
            provider.ConversationUpdated += OnProviderConversationUpdated;
        }
    }
    
    private void OnProviderConversationUpdated(object? sender, ConversationUpdatedEvent e)
    {
        // Forward provider events to AutomatonMcpClient subscribers
        ConversationUpdated?.Invoke(this, e);
    }

    /// <summary>
    /// Set the tool provider for executing MCP tools (allows dynamic tool provider selection)
    /// </summary>
    public void SetToolProvider(IMcpToolProvider? toolProvider)
    {
        _toolProvider = toolProvider;
        _logger.LogInformation("Tool provider set: {ToolProviderType}", 
            toolProvider?.GetType().Name ?? "null");
    }

    public async Task<ConversationResult> StartConversationAsync(ConversationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting conversation {ConversationId} with provider {Provider}", 
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
            _logger.LogError(ex, "Error starting conversation {ConversationId}", request.ConversationId);
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
        _logger.LogInformation("Continuing conversation {ConversationId}", conversationId);
        
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
            _logger.LogInformation("Ending conversation {ConversationId}", conversationId);
            _conversations.TryRemove(conversationId, out _);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending conversation {ConversationId}", conversationId);
            return Task.FromResult(false);
        }
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
            _logger.LogInformation("Calling AI provider {Provider} with model {Model}", request.Provider, request.Model);
            
            string responseText;
            
            // Check if we have a provider for the requested type (string-based lookup)
            var provider = _providerRegistry.GetProvider(request.Provider);
            if (provider != null)
            {
                // Get tools filtered for the current mode
                var mode = ConversationModeFactory.From(request.Mode);
                var providerTools = await CreateProviderToolsAsync(mode);
                var allowedToolNames = new HashSet<string>(providerTools.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

                // Apply mode-specific iteration limit
                request.MaxIterations = mode.MaxToolIterations;

                // Call the provider
                responseText = await provider.CallAsync(
                    request,
                    providerTools,
                    async (toolCallId, toolName, argsJson) =>
                    {
                        if (!allowedToolNames.Contains(toolName))
                        {
                            _logger.LogWarning("Tool '{ToolName}' is not available in '{Mode}' mode — call rejected", toolName, request.Mode);
                            var modeMsg = request.Mode == "plan"
                                ? $"Tool '{toolName}' is not available in plan mode. Stop attempting to call write tools. Tell the user: 'The plan is ready. Click **Implement Plan** to start implementation, or switch to Agent mode manually.'"
                                : $"Tool '{toolName}' is not available in {request.Mode} mode.";
                            return $"{{\"error\":\"{modeMsg}\"}}";
                        }
                        return await ExecuteMcpToolAsync(toolCallId, toolName, argsJson, request);
                    },
                    cancellationToken);
            }
            else
            {
                var implementedProviders = string.Join(", ", _providerRegistry.GetImplementedProviderNames());
                _logger.LogWarning("Provider {Provider} not implemented. Available: {Available}", request.Provider, implementedProviders);
                responseText = $"Provider '{request.Provider}' is not implemented. Available providers: {implementedProviders}";
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
            _logger.LogError(ex, "Error in direct AI conversation");
            
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
    private async Task<List<ProviderTool>> CreateProviderToolsAsync(IConversationMode mode)
    {
        if (_toolProvider == null)
        {
            _logger.LogWarning("No tool provider configured, returning empty tool list");
            return new List<ProviderTool>();
        }

        var mcpTools = await _toolProvider.GetToolsAsync();

        return mcpTools
            .Where(mode.IsToolAllowed)
            .Select(tool => new ProviderTool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchemaJson = tool.InputSchema?.RootElement.GetRawText() ?? "{}"
            })
            .ToList();
    }

    /// <summary>
    /// Execute an MCP tool call using the integrated tool provider
    /// </summary>
    private async Task<string> ExecuteMcpToolAsync(string toolCallId, string toolName, string argumentsJson, ConversationRequest request)
    {
        try
        {
            if (_toolProvider == null)
            {
                _logger.LogWarning("No tool provider configured, cannot execute tool {ToolName}", toolName);
                return $"Error: No tool provider configured for executing {toolName}";
            }

            var arguments = JsonDocument.Parse(string.IsNullOrEmpty(argumentsJson) ? "{}" : argumentsJson);

            // Inject conversation mode into tool context so tools can adapt behavior
            var toolContext = request.Context != null
                ? new Dictionary<string, object>(request.Context)
                : new Dictionary<string, object>();
            toolContext["mode"] = request.Mode ?? "agent";

            var contextId = request.Context?.GetValueOrDefault("contextId")?.ToString();
            var result = await _toolProvider.ExecuteToolAsync(toolName, arguments.RootElement, contextId, toolContext);

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
            _logger.LogError(ex, "Error executing MCP tool {ToolName}", toolName);
            return $"Error executing tool {toolName}: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
        }
    }

    public void Dispose()
    {
        // Clean up resources
        _conversations.Clear();
    }
}
