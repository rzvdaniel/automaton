using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Automaton.Studio.Pages.FlowDesigner.Components;

public enum FlowMessageType
{
    Text,
    Generating,
    FlowSuccess,
    FlowError,
    System
}

public partial class FlowAiChatPanel : ComponentBase, IDisposable
{
    [Parameter] public EventCallback<string> OnSend { get; set; }
    [Parameter] public EventCallback<string> OnFlowGenerated { get; set; }
    [Parameter] public List<string>? AvailableSteps { get; set; }
    [Parameter] public List<string>? AvailableCustomSteps { get; set; }
    [Parameter] public Func<Task<string>>? GetCurrentFlow { get; set; }

    [Inject] private AiApiKeyService AiApiKeyService { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    protected ElementReference messagesContainer;

    private string selectedAgent = Ask;
    private string selectedProviderModel = string.Empty;
    private string promptText = string.Empty;
    private bool showNoProvidersMessage = false;
    private bool isGeneratingFlow = false;
    private Dictionary<string, string> pendingFlows = new();
    private List<FlowVersion> flowHistory = new();

    private List<AiProviderInfo>? availableProviders;

    private List<FlowChatMessage> messages = new()
    {
        new FlowChatMessage("System", "AI flow generation panel ready. Select a provider and model to start generating flows.", DateTime.Now) 
        { 
            Type = FlowMessageType.System 
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
            messages.Add(new FlowChatMessage("System", $"Error loading AI providers: {ex.Message}", DateTime.Now)
            {
                Type = FlowMessageType.FlowError
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
            if (string.IsNullOrWhiteSpace(promptText) || string.IsNullOrEmpty(selectedProviderModel) || isGeneratingFlow)
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
            if (string.IsNullOrEmpty(promptText?.Trim()) || string.IsNullOrEmpty(selectedProviderModel) || isGeneratingFlow)
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
            isGeneratingFlow = true;

            // Clear any pending apply state when starting new generation
            pendingFlows.Clear();

            // Add user message to chat
            messages.Add(new FlowChatMessage("User", System.Net.WebUtility.HtmlEncode(promptText), DateTime.Now));
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
            var generatingMessage = new FlowChatMessage("AI", contextMessage, DateTime.Now) 
            { 
                IsGenerating = true, 
                Type = FlowMessageType.Generating 
            };
            messages.Add(generatingMessage);
            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();

            // Create flow generation request
            var flowGenRequest = new FlowGenerationRequest
            {
                Description = userPrompt,
                Provider = selectedProvider!.Provider,
                SelectedAiApiKeyId = string.IsNullOrEmpty(selectedApiKeyId) ? null : Guid.Parse(selectedApiKeyId),
                Model = selectedModel,
                AvailableSteps = AvailableSteps?.ToList(),
                AvailableCustomSteps = AvailableCustomSteps?.ToList(),
                ConversationHistory = await BuildConversationHistoryAsync()
            };

            // TODO: Call the flow generation service when implemented
            // var response = await AiApiKeyService.GenerateFlowAsync(flowGenRequest);

            // For now, simulate a response
            await Task.Delay(2000); // Simulate API call

            // Remove the generating message
            messages.RemoveAt(messages.Count - 1);

            // Simulate successful response for now
            var simulatedResponse = new FlowGenerationResponse
            {
                IsSuccess = true,
                GeneratedFlow = "Flow configuration will be generated here",
                ErrorMessage = null
            };

            if (simulatedResponse.IsSuccess)
            {
                // Different behavior based on selected agent mode
                if (selectedAgent == Ask)
                {
                    // Ask mode: Show flow with apply button, don't auto-apply
                    var flowId = Guid.NewGuid().ToString();
                    var flowMessage = new FlowChatMessage("AI", "Flow generated successfully:", DateTime.Now)
                    {
                        Type = FlowMessageType.FlowSuccess,
                        FlowId = flowId,
                        GeneratedFlow = simulatedResponse.GeneratedFlow,
                        IsSuccess = true
                    };
                    
                    messages.Add(flowMessage);
                    
                    // Store the flow for later application with unique ID
                    pendingFlows[flowId] = simulatedResponse.GeneratedFlow;
                    
                    // Track in version history
                    flowHistory.Add(new FlowVersion(
                        simulatedResponse.GeneratedFlow, 
                        DateTime.Now, 
                        userPrompt.Length > 50 ? userPrompt[..47] + "..." : userPrompt,
                        false));
                }
                else
                {
                    // Agent mode: Show flow and auto-apply (existing behavior)
                    var flowMessage = new FlowChatMessage("AI", "Flow generated and applied:", DateTime.Now)
                    {
                        Type = FlowMessageType.FlowSuccess,
                        GeneratedFlow = simulatedResponse.GeneratedFlow,
                        IsSuccess = true
                    };
                    
                    messages.Add(flowMessage);

                    // Auto-apply flow in Agent mode
                    if (OnFlowGenerated.HasDelegate)
                    {
                        await OnFlowGenerated.InvokeAsync(simulatedResponse.GeneratedFlow);
                    }
                }
            }
            else
            {
                // Add error response to chat
                var errorMessage = new FlowChatMessage("AI", "Flow generation failed:", DateTime.Now)
                {
                    Type = FlowMessageType.FlowError,
                    Content = simulatedResponse.ErrorMessage ?? "Unknown error",
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
            if (messages.LastOrDefault()?.Type == FlowMessageType.Generating)
            {
                messages.RemoveAt(messages.Count - 1);
            }

            var errorMessage = new FlowChatMessage("AI", "Unexpected error:", DateTime.Now)
            {
                Type = FlowMessageType.FlowError,
                Content = ex.Message,
                IsSuccess = false
            };
            
            messages.Add(errorMessage);
        }
        finally
        {
            isGeneratingFlow = false;
            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();
        }
    }

    private async Task ScrollToBottom()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("eval", "var element = document.getElementById('flow-ai-chat-messages'); if(element) { element.scrollTop = element.scrollHeight; }");
        }
        catch
        {
            // Ignore JS interop errors
        }
    }

    private record FlowChatMessage(string Role, string Content, DateTime Time)
    {
        public bool IsGenerating { get; init; } = false;
        public string? FlowId { get; init; }
        public bool IsSuccess { get; init; } = true;
        public string? GeneratedFlow { get; init; }
        public FlowMessageType Type { get; init; } = FlowMessageType.Text;
    }

    private record FlowVersion(string Flow, DateTime Timestamp, string Description, bool WasApplied);

    // Placeholder models for flow generation
    private record FlowGenerationRequest
    {
        public string Description { get; set; } = string.Empty;
        public AiProvider Provider { get; set; }
        public Guid? SelectedAiApiKeyId { get; set; }
        public string Model { get; set; } = string.Empty;
        public List<string>? AvailableSteps { get; set; }
        public List<string>? AvailableCustomSteps { get; set; }
        public List<ConversationMessage> ConversationHistory { get; set; } = new();
    }

    private record FlowGenerationResponse
    {
        public bool IsSuccess { get; set; }
        public string? GeneratedFlow { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private const string Agent = "Agent";
    private const string Ask = "Ask";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var objRef = DotNetObjectReference.Create(this);

            // Store the component reference globally first
            await JSRuntime.InvokeVoidAsync("eval", "window.flowAiChatPanelInstance = null;");

            // Set the reference using a separate call
            await JSRuntime.InvokeVoidAsync("setFlowAiChatPanelRef", objRef);

            // Register the apply flow function
            await JSRuntime.InvokeVoidAsync("eval", @"
                window.applyFlow = function(flowId) {
                    if (window.flowAiChatPanelInstance) {
                        window.flowAiChatPanelInstance.invokeMethodAsync('ApplyFlow', flowId);
                    }
                };
            ");
        }
    }

    [JSInvokable]
    public async Task ApplyFlow(string flowId)
    {
        await ApplyFlowFromButton(flowId);
    }

    private async Task ApplyFlowFromButton(string flowId)
    {
        if (pendingFlows.TryGetValue(flowId, out var flow) && OnFlowGenerated.HasDelegate)
        {
            await OnFlowGenerated.InvokeAsync(flow);

            // Remove the applied flow from pending
            pendingFlows.Remove(flowId);

            // Add confirmation message
            messages.Add(new FlowChatMessage("System", "Flow applied to designer", DateTime.Now) 
            { 
                Type = FlowMessageType.System 
            });
            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();
        }
    }

    private async Task<List<ConversationMessage>> BuildConversationHistoryAsync()
    {
        var conversationHistory = new List<ConversationMessage>();
        
        // Add current flow context as system message if available
        if (GetCurrentFlow != null)
        {
            try
            {
                var currentFlow = await GetCurrentFlow();
                if (!string.IsNullOrEmpty(currentFlow))
                {
                    conversationHistory.Add(new ConversationMessage("system", 
                        $"Current flow in the designer:\n\n```json\n{currentFlow}\n```\n\n" +
                        "When the user asks for modifications, improvements, or changes, please consider this existing flow as the starting point. " +
                        "If the user wants to add functionality, build upon this existing flow. If they want to fix issues, modify the relevant parts. " +
                        "Always provide complete, working flow configuration that includes both the existing functionality and the requested changes."));
                }
            }
            catch
            {
                // If getting current flow fails, continue without flow context
            }
        }
        
        // Add available steps context
        if (AvailableSteps?.Any() == true || AvailableCustomSteps?.Any() == true)
        {
            var stepsContext = "Available steps for flow generation:\n";
            if (AvailableSteps?.Any() == true)
            {
                stepsContext += $"Core Steps: {string.Join(", ", AvailableSteps)}\n";
            }
            if (AvailableCustomSteps?.Any() == true)
            {
                stepsContext += $"Custom Steps: {string.Join(", ", AvailableCustomSteps)}\n";
            }
            stepsContext += "\nPlease use these available steps when generating flows.";
            
            conversationHistory.Add(new ConversationMessage("system", stepsContext));
        }
        
        // Apply smart conversation history filtering
        var relevantMessages = FilterConversationHistory();
        
        foreach (var message in relevantMessages)
        {
            // Skip system messages and generating messages
            if (message.Type == FlowMessageType.System || message.Type == FlowMessageType.Generating)
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
                // For successful flow generation, include intent but not always full flow
                if (message.Type == FlowMessageType.FlowSuccess && !string.IsNullOrEmpty(message.GeneratedFlow))
                {
                    // Include flow only for recent messages or when explicitly referenced
                    if (ShouldIncludeFlowInHistory(message))
                    {
                        content = $"{message.Content}\n\n```json\n{message.GeneratedFlow}\n```";
                    }
                    else
                    {
                        content = $"{message.Content} [Flow generated - {message.GeneratedFlow.Length} chars]";
                    }
                }
                // For errors, include the error message
                else if (message.Type == FlowMessageType.FlowError)
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

    private List<FlowChatMessage> FilterConversationHistory()
    {
        const int maxMessages = 10; // Keep last 10 exchanges
        const int maxTokens = 8000; // Rough token limit
        
        var allMessages = messages.Where(m => 
            m.Type != FlowMessageType.System && 
            m.Type != FlowMessageType.Generating).ToList();
            
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
    
    private bool ShouldIncludeFlowInHistory(FlowChatMessage message)
    {
        // Include flow in history only for:
        // 1. Last 3 AI responses
        // 2. Messages from last 5 minutes
        // 3. When flow is small (< 500 chars)
        
        var recentMessages = messages.Where(m => 
            m.Type == FlowMessageType.FlowSuccess && 
            m.Role == "AI").TakeLast(3);
            
        if (recentMessages.Contains(message))
            return true;
            
        if (DateTime.Now - message.Time < TimeSpan.FromMinutes(5))
            return true;
            
        if (message.GeneratedFlow?.Length < 500)
            return true;
            
        return false;
    }
    
    private int EstimateTokenCount(List<FlowChatMessage> messages)
    {
        // Rough estimation: 1 token ? 4 characters
        return messages.Sum(m => 
            (m.Content?.Length ?? 0) + 
            (m.GeneratedFlow?.Length ?? 0)) / 4;
    }

    private async Task ClearConversationHistory()
    {
        if (isGeneratingFlow) return;

        // Clear all messages except for system messages
        messages.RemoveAll(m => m.Type != FlowMessageType.System);
        
        // Clear pending flows
        pendingFlows.Clear();

        // Add a system message about the cleared history
        messages.Add(new FlowChatMessage("System", "Conversation history cleared. Starting fresh.", DateTime.Now) 
        { 
            Type = FlowMessageType.System 
        });

        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
    }

    public void Dispose()
    {
        // Clean up JavaScript references
        try
        {
            JSRuntime.InvokeVoidAsync("eval", "delete window.applyFlow; delete window.flowAiChatPanelInstance; delete window.setFlowAiChatPanelRef;");
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}