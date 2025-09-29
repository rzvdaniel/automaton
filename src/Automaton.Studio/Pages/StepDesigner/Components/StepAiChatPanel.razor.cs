using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Automaton.Studio.Pages.StepDesigner.Components;

public enum MessageType
{
    Text,
    Generating,
    CodeSuccess,
    CodeError,
    System
}

public partial class StepAiChatPanel : ComponentBase, IDisposable
{
    [Parameter] public EventCallback<string> OnSend { get; set; }
    [Parameter] public EventCallback<string> OnCodeGenerated { get; set; }
    [Parameter] public string CurrentLanguage { get; set; } = "Python";
    [Parameter] public List<VariableInfo>? InputVariables { get; set; }
    [Parameter] public List<VariableInfo>? OutputVariables { get; set; }
    [Parameter] public Func<Task<string>>? GetCurrentCode { get; set; }

    [Inject] private AiApiKeyService AiApiKeyService { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    protected ElementReference messagesContainer;

    private string selectedAgent = Ask;
    private string selectedProviderModel = string.Empty;
    private string promptText = string.Empty;
    private bool showNoProvidersMessage = false;
    private bool isGeneratingCode = false;
    private Dictionary<string, string> pendingCodes = new();
    private List<CodeVersion> codeHistory = new();

    private List<AiProviderInfo>? availableProviders;

    private List<ChatMessage> messages = new()
    {
        new ChatMessage("System", "AI code panel ready. Select a provider and model to start generating code.", DateTime.Now) 
        { 
            Type = MessageType.System 
        }
    };

    protected override async Task OnInitializedAsync()
    {
        await LoadProvidersAsync();
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
        if (Enum.TryParse<AiProvider>(providerName, out var provider))
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
            var contextMessage = $"Using {selectedProvider?.ProviderName} - {selectedModel}";
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

            // Create code generation request
            var codeGenRequest = new CodeGenerationRequest
            {
                Description = userPrompt,
                Language = CurrentLanguage ?? "Python",
                Provider = selectedProvider!.Provider,
                SelectedAiApiKeyId = string.IsNullOrEmpty(selectedApiKeyId) ? null : Guid.Parse(selectedApiKeyId),
                Model = selectedModel,
                InputVariables = InputVariables?.ToList(),
                OutputVariables = OutputVariables?.ToList(),
                ConversationHistory = await BuildConversationHistoryAsync()
            };

            // Call the code generation service
            var response = await AiApiKeyService.GenerateCodeAsync(codeGenRequest);

            // Remove the generating message
            messages.RemoveAt(messages.Count - 1);

            if (response.IsSuccess)
            {
                // Different behavior based on selected agent mode
                if (selectedAgent == Ask)
                {
                    // Ask mode: Show code with apply button, don't auto-apply
                    var codeId = Guid.NewGuid().ToString();
                    var codeMessage = new ChatMessage("AI", "Code generated successfully:", DateTime.Now)
                    {
                        Type = MessageType.CodeSuccess,
                        CodeId = codeId,
                        GeneratedCode = response.GeneratedCode,
                        IsSuccess = true
                    };
                    
                    messages.Add(codeMessage);
                    
                    // Store the code for later application with unique ID
                    pendingCodes[codeId] = response.GeneratedCode;
                    
                    // Track in version history
                    codeHistory.Add(new CodeVersion(
                        response.GeneratedCode, 
                        DateTime.Now, 
                        userPrompt.Length > 50 ? userPrompt[..47] + "..." : userPrompt,
                        false));
                }
                else
                {
                    // Agent mode: Show code and auto-apply (existing behavior)
                    var codeMessage = new ChatMessage("AI", "Code generated and applied:", DateTime.Now)
                    {
                        Type = MessageType.CodeSuccess,
                        GeneratedCode = response.GeneratedCode,
                        IsSuccess = true
                    };
                    
                    messages.Add(codeMessage);

                    // Auto-apply code in Agent mode
                    if (OnCodeGenerated.HasDelegate)
                    {
                        await OnCodeGenerated.InvokeAsync(response.GeneratedCode);
                    }
                }
            }
            else
            {
                // Add error response to chat
                var errorMessage = new ChatMessage("AI", "Code generation failed:", DateTime.Now)
                {
                    Type = MessageType.CodeError,
                    Content = response.ErrorMessage,
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

    private async Task<List<ConversationMessage>> BuildConversationHistoryAsync()
    {
        var conversationHistory = new List<ConversationMessage>();
        
        // Add current code context as system message if available
        if (GetCurrentCode != null)
        {
            try
            {
                var currentCode = await GetCurrentCode();
                if (!string.IsNullOrEmpty(currentCode))
                {
                    conversationHistory.Add(new ConversationMessage("system", 
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

            conversationHistory.Add(new ConversationMessage(role, content));
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

        // Clear all messages except for system messages
        messages.RemoveAll(m => m.Type != MessageType.System);
        
        // Clear pending codes
        pendingCodes.Clear();

        // Add a system message about the cleared history
        messages.Add(new ChatMessage("System", "Conversation history cleared. Starting fresh.", DateTime.Now) 
        { 
            Type = MessageType.System 
        });

        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
    }

    public void Dispose()
    {
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
