using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Automaton.Mcp.Client;
using Automaton.Mcp.Client.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Pages.StepDesigner.Components;

public enum MessageType
{
    Text,
    Generating,
    CodeSuccess,
    CodeError,
    System,
    ToolExecution  // NEW: For MCP tool executions
}

public partial class StepAiChatPanel : ComponentBase, IDisposable
{
    [Parameter] public EventCallback<string> OnSend { get; set; }
    [Parameter] public EventCallback<string> OnCodeGenerated { get; set; }
    [Parameter] public string CurrentLanguage { get; set; } = "Python";
    [Parameter] public List<VariableInfo>? InputVariables { get; set; }
    [Parameter] public List<VariableInfo>? OutputVariables { get; set; }
    [Parameter] public Func<Task<string>>? GetCurrentCode { get; set; }
    [Parameter] public Func<string, Task<ExecutionResult>>? ExecuteCode { get; set; }  // NEW: Frontend execution
    [Parameter] public string? CurrentStepId { get; set; }  // NEW: For MCP tools

    [Inject] private AiApiKeyService AiApiKeyService { get; set; } = default!;
    [Inject] private AiStepGenerationService AiStepGenerationService { get; set; } = default!;
    [Inject] private IAutomatonMcpClient McpClient { get; set; } = default!;  // NEW: MCP client
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private ILogger<StepAiChatPanel> logger { get; set; } = default!;  // NEW: Logger

    protected ElementReference messagesContainer;

    private string selectedAgent = Agent;  // Default to Agent mode for MCP
    private string selectedProviderModel = string.Empty;
    private string promptText = string.Empty;
    private bool showNoProvidersMessage = false;
    private bool isGeneratingCode = false;
    private Dictionary<string, string> pendingCodes = new();
    private List<CodeVersion> codeHistory = new();
    private string? _conversationId;  // NEW: Track MCP conversation

    private List<AiProviderInfo>? availableProviders;

    private List<ChatMessage> messages = new()
    {
        new ChatMessage("System", "AI code panel ready with MCP integration. AI can now see and test your code in real-time.", DateTime.Now) 
        { 
            Type = MessageType.System 
        }
    };

    protected override async Task OnInitializedAsync()
    {
        // Subscribe to real-time MCP conversation updates
        McpClient.ConversationUpdated += OnConversationUpdated;
        
        await LoadProvidersAsync();
    }

    // NEW: Handle real-time conversation updates from MCP client
    private void OnConversationUpdated(object? sender, ConversationUpdatedEvent e)
    {
        if (e.ConversationId == _conversationId)
        {
            InvokeAsync(() =>
            {
                // Add tool execution messages
                foreach (var toolResult in e.ToolResults)
                {
                    var toolCall = e.ToolCalls.FirstOrDefault(tc => tc.Id == toolResult.ToolCallId);
                    var toolName = toolCall?.Name ?? "Unknown Tool";
                    
                    var toolMessage = new ChatMessage("Tool", FormatToolExecution(toolName, toolResult), DateTime.Now)
                    {
                        Type = MessageType.ToolExecution,
                        IsSuccess = toolResult.IsSuccess,
                        ToolName = toolName,
                        ToolResult = toolResult.Result
                    };
                    
                    // Check if this tool execution message already exists
                    if (!messages.Any(m => m.Type == MessageType.ToolExecution && 
                                          m.ToolName == toolName && 
                                          m.ToolResult == toolResult.Result))
                    {
                        messages.Add(toolMessage);
                    }
                }

                StateHasChanged();
                ScrollToBottom();
            });
        }
    }

    private string FormatToolExecution(string toolName, ToolResult toolResult)
    {
        var status = toolResult.IsSuccess ? "✅" : "❌";
        var summary = toolName switch
        {
            "get_current_code" => "Retrieved current code from editor",
            "execute_code" => toolResult.IsSuccess ? "Code executed successfully" : "Code execution failed",
            _ => "Tool executed"
        };

        return $"{status} {summary}";
    }

    private async Task LoadProvidersAsync()
    {
        try
        {
            availableProviders = (await AiApiKeyService.GetAvailableProvidersAsync(includeEnvironment: true)).ToList();

            showNoProvidersMessage = availableProviders?.Any(p => p.HasStoredKeys || p.HasEnvironmentKey) != true;

            // Auto-select first available provider/model if available
            if (availableProviders?.Any() == true)
            {
                var firstAvailableProvider = availableProviders.FirstOrDefault(p => (p.HasStoredKeys || p.HasEnvironmentKey) && p.SupportedModels?.Any() == true);
                if (firstAvailableProvider != null && firstAvailableProvider.SupportedModels?.Any() == true)
                {
                    selectedProviderModel = $"{firstAvailableProvider.Provider}:{firstAvailableProvider.SupportedModels.First()}";
                }
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            messages.Add(new ChatMessage("System", $"Error loading AI providers: {ex.Message}", DateTime.Now)
            {
                Type = MessageType.CodeError
            });
            StateHasChanged();
        }
    }

    private AiProviderInfo? GetSelectedProvider()
    {
        if (string.IsNullOrEmpty(selectedProviderModel) || !selectedProviderModel.Contains(':'))
            return null;

        var providerName = selectedProviderModel.Split(':')[0];
        if (Enum.TryParse<Automaton.Studio.Models.AiProvider>(providerName, out var provider))
        {
            return availableProviders?.FirstOrDefault(p => p.Provider == provider);
        }
        return null;
    }

    private string GetSelectedModel()
    {
        if (string.IsNullOrEmpty(selectedProviderModel) || !selectedProviderModel.Contains(':'))
            return string.Empty;

        return selectedProviderModel.Split(':')[1];
    }

    private string? GetSelectedApiKeyId()
    {
        var selectedProvider = GetSelectedProvider();
        if (selectedProvider == null) return null;

        // Auto-select the most recently used key, or first available key
        if (selectedProvider.StoredKeys?.Any() == true)
        {
            var mostRecentKey = selectedProvider.StoredKeys
                .OrderByDescending(k => k.LastUsedAt ?? k.CreatedAt)
                .First();
            return mostRecentKey.Id.ToString();
        }

        return null;
    }

    private bool IsReadyToSend
    {
        get
        {
            if (string.IsNullOrWhiteSpace(promptText) || string.IsNullOrEmpty(selectedProviderModel) || isGeneratingCode)
                return false;

            var selectedProvider = GetSelectedProvider();
            if (selectedProvider == null)
                return false;

            // If provider has stored keys, we need at least one available
            // If provider has environment key, it's ready
            return selectedProvider.HasStoredKeys || selectedProvider.HasEnvironmentKey;
        }
    }

    private bool IsReadyToSendRealtime
    {
        get
        {
            if (string.IsNullOrEmpty(promptText?.Trim()) || string.IsNullOrEmpty(selectedProviderModel) || isGeneratingCode)
                return false;

            var selectedProvider = GetSelectedProvider();
            if (selectedProvider == null)
                return false;

            // If provider has stored keys, we need at least one available
            // If provider has environment key, it's ready
            return selectedProvider.HasStoredKeys || selectedProvider.HasEnvironmentKey;
        }
    }

    private void OnInputChange(ChangeEventArgs e)
    {
        promptText = e.Value?.ToString() ?? string.Empty;
        StateHasChanged(); // Trigger re-render to update button state
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && (e.CtrlKey || e.ShiftKey))
        {
            await SendPrompt();
        }
    }

    private async Task SendPrompt()
    {
        if (!IsReadyToSend) return;

        try
        {
            isGeneratingCode = true;

            // Clear any pending apply state when starting new generation
            pendingCodes.Clear();

            // Add user message to chat
            messages.Add(new ChatMessage("User", System.Net.WebUtility.HtmlEncode(promptText), DateTime.Now));
            var userPrompt = promptText;
            promptText = string.Empty;

            var selectedProvider = GetSelectedProvider();
            var selectedModel = GetSelectedModel();
            var selectedApiKeyId = GetSelectedApiKeyId();

            // Add context about selected provider/model
            var contextMessage = $"Using {selectedProvider?.ProviderName} - {selectedModel} with frontend tools";
            if (!string.IsNullOrEmpty(selectedApiKeyId) && selectedProvider?.StoredKeys?.Any() == true)
            {
                var selectedKey = selectedProvider.StoredKeys.FirstOrDefault(k => k.Id.ToString() == selectedApiKeyId);
                if (selectedKey != null)
                {
                    contextMessage += $" (Key: {selectedKey.Name})";
                }
            }

            // Add generating message
            var generatingMessage = new ChatMessage("AI", contextMessage, DateTime.Now) 
            { 
                IsGenerating = true, 
                Type = MessageType.Generating 
            };
            messages.Add(generatingMessage);
            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();

            // NEW: Use MCP client directly with frontend execution functions
            var conversationRequest = new ConversationRequest
            {
                InitialMessage = userPrompt,
                SystemPrompt = BuildSystemPrompt(),
                Provider = MapToMcpProvider(selectedProvider!.Provider),
                ApiKey = await GetApiKeyFromProvider(selectedProvider, selectedApiKeyId),
                Model = selectedModel,
                ConversationHistory = BuildMcpConversationHistory(),
                Context = new Dictionary<string, object>
                {
                    ["language"] = CurrentLanguage,
                    ["inputVariables"] = InputVariables ?? new List<VariableInfo>(),
                    ["outputVariables"] = OutputVariables ?? new List<VariableInfo>(),
                    // Pass frontend functions to MCP tools
                    ["getCurrentCode"] = GetCurrentCode ?? (() => Task.FromResult(string.Empty)),
                    ["executeCode"] = ExecuteCode ?? ((code) => Task.FromResult(new ExecutionResult 
                    { 
                        IsSuccess = false, 
                        ErrorMessage = "Execution not available" 
                    }))
                }
            };

            var result = await McpClient.StartConversationAsync(conversationRequest);
            _conversationId = result.ConversationId;

            // Remove the generating message
            messages.RemoveAt(messages.Count - 1);

            if (result.IsSuccess)
            {
                // Different behavior based on selected agent mode
                if (selectedAgent == Ask)
                {
                    // Ask mode: Show code with apply button, don't auto-apply
                    var codeId = Guid.NewGuid().ToString();
                    var codeMessage = new ChatMessage("AI", "Code generated successfully with frontend tools:", DateTime.Now)
                    {
                        Type = MessageType.CodeSuccess,
                        CodeId = codeId,
                        GeneratedCode = result.GeneratedCode,
                        IsSuccess = true
                    };
                    
                    messages.Add(codeMessage);
                    
                    // Store the code for later application with unique ID
                    pendingCodes[codeId] = result.GeneratedCode ?? string.Empty;
                    
                    // Track in version history
                    codeHistory.Add(new CodeVersion(
                        result.GeneratedCode ?? string.Empty, 
                        DateTime.Now, 
                        userPrompt.Length > 50 ? userPrompt[..47] + "..." : userPrompt,
                        false));
                }
                else
                {
                    // Agent mode: Show code and auto-apply (existing behavior)
                    var codeMessage = new ChatMessage("AI", "Code generated and applied with frontend tools:", DateTime.Now)
                    {
                        Type = MessageType.CodeSuccess,
                        GeneratedCode = result.GeneratedCode,
                        IsSuccess = true
                    };
                    
                    messages.Add(codeMessage);

                    // Auto-apply code in Agent mode
                    if (OnCodeGenerated.HasDelegate && !string.IsNullOrEmpty(result.GeneratedCode))
                    {
                        await OnCodeGenerated.InvokeAsync(result.GeneratedCode);
                    }
                }
            }
            else
            {
                // Add error response to chat
                var errorMessage = new ChatMessage("AI", "Code generation failed:", DateTime.Now)
                {
                    Type = MessageType.CodeError,
                    Content = result.ErrorMessage ?? "Unknown error",
                    IsSuccess = false
                };
                
                messages.Add(errorMessage);
            }

            // Emit event for legacy compatibility if needed
            if (OnSend.HasDelegate)
            {
                await OnSend.InvokeAsync($"{contextMessage}|{userPrompt}");
            }
        }
        catch (Exception ex)
        {
            // Remove generating message if it exists
            if (messages.LastOrDefault()?.Type == MessageType.Generating)
            {
                messages.RemoveAt(messages.Count - 1);
            }

            var errorMessage = new ChatMessage("AI", "Unexpected error:", DateTime.Now)
            {
                Type = MessageType.CodeError,
                Content = ex.Message,
                IsSuccess = false
            };
            
            messages.Add(errorMessage);
        }
        finally
        {
            isGeneratingCode = false;
            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();
        }
    }

    // NEW: Helper methods for MCP integration
    private string BuildSystemPrompt()
    {
        return $"""
            You are an expert automation developer. Generate {CurrentLanguage} code for a custom step in an automation workflow.

            IMPORTANT WORKFLOW INSTRUCTIONS:
            When tools are available, follow this process:
            1. ALWAYS start by using get_current_code to retrieve the current code from the step editor
            2. Analyze the user's request and the current code
            3. Generate or modify the code as needed
            4. Use execute_code to test the code and check if it works correctly
            5. If the execution shows errors or issues, modify the code and test again
            6. Only stop when the code executes successfully or when you're confident it meets the requirements
            7. When finished, provide the final working code as your response

            Requirements:
            - Generate clean, robust code with proper error handling
            - Use the specified language: {CurrentLanguage}
            - The code should be suitable for automation workflows
            - Follow best practices for the chosen language
            """;
    }

    private static Automaton.Mcp.Client.Models.AiProvider MapToMcpProvider(Automaton.Studio.Models.AiProvider provider)
    {
        return provider switch
        {
            Automaton.Studio.Models.AiProvider.OpenAI => Automaton.Mcp.Client.Models.AiProvider.OpenAI,
            Automaton.Studio.Models.AiProvider.Anthropic => Automaton.Mcp.Client.Models.AiProvider.Anthropic,
            Automaton.Studio.Models.AiProvider.Google => Automaton.Mcp.Client.Models.AiProvider.Google,
            Automaton.Studio.Models.AiProvider.Azure => Automaton.Mcp.Client.Models.AiProvider.Azure,
            Automaton.Studio.Models.AiProvider.Custom => Automaton.Mcp.Client.Models.AiProvider.Custom,
            _ => Automaton.Mcp.Client.Models.AiProvider.OpenAI
        };
    }

    private async Task<string> GetApiKeyFromProvider(AiProviderInfo provider, string? selectedApiKeyId)
    {
        if (!string.IsNullOrEmpty(selectedApiKeyId) && provider.StoredKeys?.Any() == true)
        {
            var selectedKey = provider.StoredKeys.FirstOrDefault(k => k.Id.ToString() == selectedApiKeyId);
            if (selectedKey != null)
            {
                // Get decrypted key from server
                var decryptedKey = await AiApiKeyService.GetDecryptedApiKeyAsync(selectedKey.Id);
                if (!string.IsNullOrEmpty(decryptedKey))
                {
                    return decryptedKey;
                }
                else
                {
                    logger.LogWarning("Failed to decrypt API key {ApiKeyId}", selectedKey.Id);
                }
            }
        }

        // Fall back to environment keys (for desktop scenarios)
        if (provider.HasEnvironmentKey)
        {
            var envKeys = await AiApiKeyService.GetEnvironmentApiKeysAsync();
            if (envKeys.TryGetValue(provider.Provider, out var envKey) && !string.IsNullOrEmpty(envKey))
            {
                return envKey;
            }
        }

        logger.LogWarning("No valid API key found for provider {Provider}", provider.Provider);
        return string.Empty;
    }

    private async Task<Guid> GetCurrentUserIdAsync()
    {
        // TODO: Get from authentication context
        // For now, return a placeholder - this needs to be implemented based on your auth system
        return Guid.Empty;
    }

    private List<Automaton.Mcp.Client.Models.ConversationMessage> BuildMcpConversationHistory()
    {
        var conversationHistory = new List<Automaton.Mcp.Client.Models.ConversationMessage>();
        
        // Convert existing messages to MCP format
        foreach (var message in messages.Where(m => m.Type != MessageType.System && m.Type != MessageType.Generating && m.Type != MessageType.ToolExecution))
        {
            string role = message.Role.ToLowerInvariant() switch
            {
                "user" => "user",
                "ai" => "assistant",
                _ => "user"
            };

            string content = message.Type == MessageType.CodeSuccess && !string.IsNullOrEmpty(message.GeneratedCode)
                ? $"{message.Content}\n\n```{CurrentLanguage?.ToLowerInvariant() ?? "python"}\n{message.GeneratedCode}\n```"
                : message.Content;

            conversationHistory.Add(new Automaton.Mcp.Client.Models.ConversationMessage(role, content));
        }

        return conversationHistory;
    }

    private async Task ScrollToBottom()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("eval", "var element = document.getElementById('ai-chat-messages'); if(element) { element.scrollTop = element.scrollHeight; }");
        }
        catch
        {
            // Ignore JS interop errors
        }
    }

    private record ChatMessage(string Role, string Content, DateTime Time)
    {
        public bool IsGenerating { get; init; } = false;
        public string? CodeId { get; init; }
        public bool IsSuccess { get; init; } = true;
        public string? GeneratedCode { get; init; }
        public MessageType Type { get; init; } = MessageType.Text;
        public string? ToolName { get; init; }  // NEW: For tool execution messages
        public string? ToolResult { get; init; }  // NEW: For tool execution results
    }

    private record CodeVersion(string Code, DateTime Timestamp, string Description, bool WasApplied);

    private const string Agent = "Agent";
    private const string Ask = "Ask";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var objRef = DotNetObjectReference.Create(this);

            // Store the component reference globally first
            await JSRuntime.InvokeVoidAsync("eval", "window.aiChatPanelInstance = null;");

            // Set the reference using a separate call
            await JSRuntime.InvokeVoidAsync("setAiChatPanelRef", objRef);

            // Register the apply code function
            await JSRuntime.InvokeVoidAsync("eval", @"
                window.applyCode = function(codeId) {
                    if (window.aiChatPanelInstance) {
                        window.aiChatPanelInstance.invokeMethodAsync('ApplyCode', codeId);
                    }
                };
            ");
        }
    }

    [JSInvokable]
    public async Task ApplyCode(string codeId)
    {
        await ApplyCodeFromButton(codeId);
    }

    private async Task ApplyCodeFromButton(string codeId)
    {
        if (pendingCodes.TryGetValue(codeId, out var code) && OnCodeGenerated.HasDelegate)
        {
            await OnCodeGenerated.InvokeAsync(code);

            // Remove the applied code from pending
            pendingCodes.Remove(codeId);

            // Add confirmation message
            messages.Add(new ChatMessage("System", "Code applied to editor", DateTime.Now) 
            { 
                Type = MessageType.System 
            });
            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();
        }
    }

    private async Task<List<Automaton.Studio.Models.ConversationMessage>> BuildConversationHistoryAsync()
    {
        var conversationHistory = new List<Automaton.Studio.Models.ConversationMessage>();
        
        // Add current code context as system message if available
        if (GetCurrentCode != null)
        {
            try
            {
                var currentCode = await GetCurrentCode();
                if (!string.IsNullOrEmpty(currentCode))
                {
                    conversationHistory.Add(new Automaton.Studio.Models.ConversationMessage("system", 
                        $"Current code in the editor ({CurrentLanguage ?? "python"}):\n\n```{CurrentLanguage?.ToLowerInvariant() ?? "python"}\n{currentCode}\n```\n\n" +
                        "When the user asks for modifications, improvements, or changes, please consider this existing code as the starting point. " +
                        "If the user wants to add functionality, build upon this existing code. If they want to fix issues, modify the relevant parts. " +
                        "Always provide complete, working code that includes both the existing functionality and the requested changes."));
                }
            }
            catch
            {
                // If getting current code fails, continue without code context
            }
        }
        
        // Apply smart conversation history filtering
        var relevantMessages = FilterConversationHistory();
        
        foreach (var message in relevantMessages)
        {
            // Skip system messages and generating messages
            if (message.Type == MessageType.System || message.Type == MessageType.Generating)
                continue;

            string role;
            string content;

            if (message.Role == "User")
            {
                role = "user";
                content = message.Content;
            }
            else if (message.Role == "AI")
            {
                role = "assistant";
                // For successful code generation, include intent but not always full code
                if (message.Type == MessageType.CodeSuccess && !string.IsNullOrEmpty(message.GeneratedCode))
                {
                    // Include code only for recent messages or when explicitly referenced
                    if (ShouldIncludeCodeInHistory(message))
                    {
                        content = $"{message.Content}\n\n```{CurrentLanguage?.ToLowerInvariant() ?? "python"}\n{message.GeneratedCode}\n```";
                    }
                    else
                    {
                        content = $"{message.Content} [Code generated - {message.GeneratedCode.Length} chars]";
                    }
                }
                // For errors, include the error message
                else if (message.Type == MessageType.CodeError)
                {
                    content = $"I encountered an error: {message.Content}";
                }
                else
                {
                    content = message.Content;
                }
            }
            else
            {
                continue; // Skip unknown roles
            }

            conversationHistory.Add(new Automaton.Studio.Models.ConversationMessage(role, content));
        }

        return conversationHistory;
    }

    private List<ChatMessage> FilterConversationHistory()
    {
        const int maxMessages = 10; // Keep last 10 exchanges
        const int maxTokens = 8000; // Rough token limit
        
        var allMessages = messages.Where(m => 
            m.Type != MessageType.System && 
            m.Type != MessageType.Generating).ToList();
            
        // If we have few messages, return all
        if (allMessages.Count <= maxMessages)
        {
            return allMessages;
        }
        
        // Take recent messages and estimate token usage
        var recentMessages = allMessages.TakeLast(maxMessages).ToList();
        var estimatedTokens = EstimateTokenCount(recentMessages);
        
        // If still too long, reduce further
        while (estimatedTokens > maxTokens && recentMessages.Count > 4)
        {
            recentMessages.RemoveAt(0); // Remove oldest
            estimatedTokens = EstimateTokenCount(recentMessages);
        }
        
        return recentMessages;
    }
    
    private bool ShouldIncludeCodeInHistory(ChatMessage message)
    {
        // Include code in history only for:
        // 1. Last 3 AI responses
        // 2. Messages from last 5 minutes
        // 3. When code is small (< 500 chars)
        
        var recentMessages = messages.Where(m => 
            m.Type == MessageType.CodeSuccess && 
            m.Role == "AI").TakeLast(3);
            
        if (recentMessages.Contains(message))
            return true;
            
        if (DateTime.Now - message.Time < TimeSpan.FromMinutes(5))
            return true;
            
        if (message.GeneratedCode?.Length < 500)
            return true;
            
        return false;
    }
    
    private int EstimateTokenCount(List<ChatMessage> messages)
    {
        // Rough estimation: 1 token ≈ 4 characters
        return messages.Sum(m => 
            (m.Content?.Length ?? 0) + 
            (m.GeneratedCode?.Length ?? 0)) / 4;
    }

    private async Task ClearConversationHistory()
    {
        if (isGeneratingCode) return;

        // End current MCP conversation if active
        if (!string.IsNullOrEmpty(_conversationId))
        {
            await McpClient.EndConversationAsync(_conversationId);
            _conversationId = null;
        }

        // Clear all messages except for system messages
        messages.RemoveAll(m => m.Type != MessageType.System);
        
        // Clear pending codes
        pendingCodes.Clear();

        // Add a system message about the cleared history
        messages.Add(new ChatMessage("System", "Conversation history cleared. AI can now see and test your code in real-time.", DateTime.Now) 
        { 
            Type = MessageType.System 
        });

        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
    }

    public void Dispose()
    {
        // Unsubscribe from MCP events
        McpClient.ConversationUpdated -= OnConversationUpdated;

        // End current conversation if active
        if (!string.IsNullOrEmpty(_conversationId))
        {
            _ = Task.Run(() => McpClient.EndConversationAsync(_conversationId));
        }

        // Clean up JavaScript references
        try
        {
            JSRuntime.InvokeVoidAsync("eval", "delete window.applyCode; delete window.aiChatPanelInstance; delete window.setAiChatPanelRef;");
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
