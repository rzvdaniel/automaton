using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Automaton.Mcp.Client;
using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging;
using Automaton.Studio.Components.AiChat.ToolResults;
using Automaton.Studio.Components.AiChat.ToolResults.Processors;
using Automaton.Mcp.Enums;
using Automaton.Studio.Services.Mcp;
using Automaton.Studio.Components.AiChat.Models;
using Automaton.Studio.Components.AiChat.Contexts;

namespace Automaton.Studio.Components.AiChat;

public partial class AiChatPanel : ComponentBase, IDisposable
{
    [Parameter] public IAiChatContext Context { get; set; } = default!;
    [Parameter] public EventCallback<string> OnSend { get; set; }
    [Parameter] public EventCallback<string> OnCodeGenerated { get; set; }

    [Inject] private AiApiKeyService AiApiKeyService { get; set; } = default!;
    [Inject] private AiStepGenerationService AiStepGenerationService { get; set; } = default!;
    [Inject] private IAutomatonMcpClient McpClient { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private ILogger<AiChatPanel> logger { get; set; } = default!;
    [Inject] private ICodeExtractionService CodeExtractionService { get; set; } = default!;
    [Inject] private ITextSimilarityService TextSimilarityService { get; set; } = default!;
    [Inject] private IAiMessageFormattingService AiMessageFormattingService { get; set; } = default!;

    private const string Agent = "Agent";
    private const string Ask = "Ask";

    protected ElementReference messagesContainer;

    private string selectedAgent = Agent;
    private string selectedProviderModel = string.Empty;
    private string promptText = string.Empty;
    private bool showNoProvidersMessage = false;
    private bool isGeneratingCode = false;
    private Dictionary<string, string> pendingCodes = new();
    private List<CodeVersion> codeHistory = new();
    private string? _conversationId;
    private readonly ToolResultProcessorFactory _toolResultProcessorFactory = new();

    private List<AiProviderInfo>? availableProviders;

    private List<ChatMessage> messages = new();

    protected override async Task OnInitializedAsync()
    {
        // Initialize with context-specific system message
        messages.Add(new ChatMessage("System", Context.InitialSystemMessage, DateTime.Now) 
        { 
            Type = MessageType.System 
        });

        McpClient.ConversationUpdated += OnConversationUpdated;
        await LoadProvidersAsync();
    }

    // Handle real-time conversation updates from MCP client
    private void OnConversationUpdated(object? sender, ConversationUpdatedEvent e)
    {
        if (e.ConversationId == _conversationId)
        {
            InvokeAsync(() =>
            {
                // Process AI reasoning messages (thinking/error analysis)
                if (e.Messages?.Any() == true)
                {
                    foreach (var message in e.Messages)
                    {
                        if (message.Role.ToLowerInvariant() == "assistant" && !string.IsNullOrWhiteSpace(message.Content))
                        {
                            // Check if this message already exists in the conversation to avoid duplicates
                            var isDuplicate = messages.Any(m => 
                                m.Role == "AI" && 
                                m.Type == MessageType.Text &&
                                TextSimilarityService.AreSimilar(m.Content, message.Content, similarityThreshold: 0.95));
                            
                            if (isDuplicate)
                            {
                                logger.LogInformation("Skipping duplicate AI message: {Length} chars", message.Content.Length);
                                continue;
                            }
                            
                            // Check if this is a reasoning message (not a final code response)
                            var isReasoningMessage = IsReasoningMessage(message.Content);
                            
                            if (isReasoningMessage)
                            {
                                // Add as text message to show AI's thinking
                                var reasoningMsg = new ChatMessage("AI", message.Content, DateTime.Now)
                                {
                                    Type = MessageType.Text
                                };
                                messages.Add(reasoningMsg);
                                logger.LogInformation("Added AI reasoning message: {Length} chars", message.Content.Length);
                                InvokeAsync(StateHasChanged);
                                
                            }
                        }
                    }
                }
                
                // Process tool calls and results - ALWAYS SHOW THEM
                foreach (var toolCall in e.ToolCalls)
                {
                    var existingToolMessage = messages.FirstOrDefault(m => 
                        m.Type == MessageType.ToolExecution && 
                        m.ToolCallId == toolCall.Id);

                    if (existingToolMessage == null)
                    {
                        var toolMessage = new ChatMessage("AI", FormatToolCall(toolCall), DateTime.Now)
                        {
                            Type = MessageType.ToolExecution,
                            ToolCallId = toolCall.Id,
                            ToolName = toolCall.Name,
                            ToolStatus = "executing",
                            ToolArguments = System.Text.Json.JsonSerializer.Serialize(toolCall.Arguments)
                        };
                        
                        messages.Add(toolMessage);
                        logger.LogInformation("Added tool call message: {ToolName} - {Status}", toolCall.Name, "Executing");
                        // Update UI immediately to show tool execution
                        InvokeAsync(StateHasChanged);
                    }
                }

                // Add tool results as NEW messages (don't replace executing messages)
                foreach (var toolResult in e.ToolResults)
                {
                    // Check if we already added this result
                    var existingResultMessage = messages.FirstOrDefault(m => 
                        m.Type == MessageType.ToolExecution && 
                        m.ToolCallId == toolResult.ToolCallId &&
                        m.ToolStatus != "executing");

                    if (existingResultMessage == null)
                    {
                        // First, update the previous "executing" message to show it's complete
                        var executingMessage = messages.FirstOrDefault(m =>
                            m.Type == MessageType.ToolExecution &&
                            m.ToolCallId == toolResult.ToolCallId &&
                            m.ToolStatus == "executing");

                        if (executingMessage != null)
                        {
                            // Update the executing message to show completion status
                            var index = messages.IndexOf(executingMessage);
                            messages[index] = executingMessage with 
                            { 
                                ToolStatus = "completed",
                                IsSuccess = toolResult.IsSuccess
                            };
                        }

                        var toolCallForResult = e.ToolCalls.FirstOrDefault(tc => tc.Id == toolResult.ToolCallId);
                        if (toolCallForResult != null)
                        {
                            // Process tool result using the new architecture (only if context supports it)
                            if (Context.ProcessToolResults)
                            {
                                var processedResult = _toolResultProcessorFactory.ProcessToolResult(
                                    toolCallForResult.Name, toolResult, Context.Language);

                                // Add as NEW message to show result details
                                var resultMessage = new ChatMessage("AI", FormatToolResultFromViewModel(processedResult), DateTime.Now)
                                {
                                    Type = MessageType.ToolExecution,
                                    ToolCallId = toolResult.ToolCallId,
                                    ToolName = toolCallForResult.Name,
                                    ToolStatus = toolResult.IsSuccess ? "success" : "error",
                                    ToolResult = toolResult.Result,
                                    ToolError = toolResult.ErrorMessage,
                                    ToolResultViewModel = processedResult,
                                    IsSuccess = toolResult.IsSuccess
                                };
                                
                                messages.Add(resultMessage);
                                logger.LogInformation("Added tool result message: {ToolName} - {Status}", toolCallForResult.Name, toolResult.IsSuccess ? "Success" : "Failed");
                            }
                        }
                    }
                }

                StateHasChanged();
                ScrollToBottom();
            });
        }
    }
    
    /// <summary>
    /// Determine if a message is AI reasoning/thinking (vs final code output)
    /// </summary>
    private bool IsReasoningMessage(string content)
    {
        // Skip if it's very short (likely not reasoning)
        if (content.Length < 10)
            return false;
            
        // Skip if it contains only code (no explanation)
        if (content.StartsWith("```") && content.Count(c => c == '`') >= 6)
            return false;
            
        // Include if it contains reasoning keywords
        var reasoningKeywords = new[]
        {
            "I see", "I notice", "I'll", "Let me", "I need to",
            "The error", "The issue", "The problem", "This is because",
            "I'm going to", "I will", "First", "Next", "Now",
            "Let's", "We need", "Looking at", "Based on",
            "I can see", "It appears", "It looks like", "It seems",
            "I should", "I must", "I have to", "To fix", "To solve"
        };
        
        return reasoningKeywords.Any(keyword => 
            content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private string FormatToolCall(ToolCall toolCall)
    {
        var icon = GetToolIcon(toolCall.Name);
        var description = GetToolDescription(toolCall.Name);
        return $"{icon} {description}";
    }

    private string FormatToolResultFromViewModel(ToolResultViewModel viewModel)
    {
        // Basic text format for backward compatibility
        var result = $"{viewModel.Icon} {viewModel.Description} {viewModel.StatusIcon} {viewModel.Status}";
        
        if (!string.IsNullOrEmpty(viewModel.ErrorMessage))
        {
            result += $"\n\n**Error:** {viewModel.ErrorMessage}";
        }
        else if (!string.IsNullOrEmpty(viewModel.Summary))
        {
            result += $"\n\n{viewModel.Summary}";
        }
        
        return result;
    }

    // Proper Unicode emoji icons - these work in browsers
    private string GetToolIcon(string toolName)
    {
        return toolName switch
        {
            "get_current_code" => "📖",    // Book emoji
            "execute_code" => "🧪",        // Test tube emoji  
            "get_step_variables" => "🔍",  // Magnifying glass emoji
            "suggest_variables" => "✨",    // Sparkles emoji (for creating variables)
            "save_code" => "💾",           // Floppy disk emoji
            _ => "🔧"                      // Wrench emoji
        };
    }

    private string GetToolDescription(string toolName)
    {
        return toolName switch
        {
            "get_current_code" => "Reading current code from editor",
            "execute_code" => "Testing code execution",
            "get_step_variables" => "Analyzing step variables",
            "suggest_variables" => "Creating input/output variables",
            "save_code" => "Saving code to step",
            _ => "Executing tool"
        };
    }

    private async Task LoadProvidersAsync()
    {
        try
        {
            availableProviders = (await AiApiKeyService.GetAvailableProvidersAsync(includeEnvironment: true)).ToList();
            showNoProvidersMessage = availableProviders?.Any(p => p.HasStoredKeys || p.HasEnvironmentKey) != true;

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
        if (Enum.TryParse<AiProviderType>(providerName, out var provider))
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

            return selectedProvider.HasStoredKeys || selectedProvider.HasEnvironmentKey;
        }
    }

    private void OnInputChange(ChangeEventArgs e)
    {
        promptText = e.Value?.ToString() ?? string.Empty;
        StateHasChanged();
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !(e.CtrlKey || e.ShiftKey))
        {
            await SendPrompt();
        }
    }

    private async Task SendPrompt()
    {
        if (!IsReadyToSend) return;

        try
        {
            await BeginConversation();
            var conversationContext = await PrepareConversationContext();
            var result = await ExecuteConversation(conversationContext);
            await ProcessConversationResult(result, conversationContext.UserPrompt);
        }
        catch (Exception ex)
        {
            HandleConversationError(ex);
        }
        finally
        {
            await EndConversation();
        }
    }

    // SRP: UI State Management
    private async Task BeginConversation()
    {
        isGeneratingCode = true;
        pendingCodes.Clear();
        
        var userMessage = new ChatMessage("User", System.Net.WebUtility.HtmlEncode(promptText), DateTime.Now);
        messages.Add(userMessage);
        
        var generatingMessage = CreateGeneratingMessage();
        messages.Add(generatingMessage);
        
        promptText = string.Empty;
        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
    }

    // SRP: Message Creation
    private ChatMessage CreateGeneratingMessage()
    {
        var selectedProvider = GetSelectedProvider();
        var selectedModel = GetSelectedModel();
        var selectedApiKeyId = GetSelectedApiKeyId();
        
        var contextMessage = BuildContextMessage(selectedProvider, selectedModel, selectedApiKeyId);
        
        return new ChatMessage("AI", contextMessage, DateTime.Now) 
        { 
            IsGenerating = true, 
            Type = MessageType.Generating 
        };
    }

    // SRP: Context Message Building
    private string BuildContextMessage(AiProviderInfo? provider, string model, string? apiKeyId)
    {
        var contextMessage = $"Using {provider?.ProviderName} - {model} with frontend tools";
        
        if (!string.IsNullOrEmpty(apiKeyId) && provider?.StoredKeys?.Any() == true)
        {
            var selectedKey = provider.StoredKeys.FirstOrDefault(k => k.Id.ToString() == apiKeyId);
            if (selectedKey != null)
            {
                contextMessage += $" (Key: {selectedKey.Name})";
            }
        }
        
        return contextMessage;
    }

    // SRP: Conversation Context Preparation
    private async Task<ConversationContext> PrepareConversationContext()
    {
        var selectedProvider = GetSelectedProvider();
        var selectedModel = GetSelectedModel();
        var selectedApiKeyId = GetSelectedApiKeyId();
        
        if (selectedProvider == null)
            throw new InvalidOperationException("No AI provider selected");
            
        var apiKey = await GetApiKeyFromProvider(selectedProvider, selectedApiKeyId);
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("No valid API key found for the specified provider");
            
        var userPrompt = messages.Last(m => m.Role == "User").Content;
        
        return new ConversationContext
        {
            UserPrompt = userPrompt,
            Provider = selectedProvider,
            Model = selectedModel,
            ApiKey = apiKey,
            ConversationRequest = BuildConversationRequest(userPrompt, selectedProvider, selectedModel, apiKey)
        };
    }

    // SRP: MCP Request Building
    private ConversationRequest BuildConversationRequest(string userPrompt, AiProviderInfo provider, string model, string apiKey)
    {
        var request = new ConversationRequest
        {
            InitialMessage = userPrompt,
            SystemPrompt = Context.BuildSystemPrompt(),
            Provider = provider.Provider,
            ApiKey = apiKey,
            Model = model,
            ConversationHistory = BuildMcpConversationHistory(),
            Context = new Dictionary<string, object>(Context.GetAdditionalContext())
            {
                ["contextId"] = Context.GetContextId()
            }
        };
        
        return request;
    }

    // SRP: MCP Conversation Execution
    private async Task<ConversationResult> ExecuteConversation(ConversationContext context)
    {
        _conversationId = context.ConversationRequest.ConversationId;
        var result = await McpClient.StartConversationAsync(context.ConversationRequest);
        _conversationId = result.ConversationId;
        
        // Clean up generating message
        RemoveGeneratingMessage();
        
        logger.LogInformation("Messages after conversation: {Count}, Tool messages: {ToolCount}", 
            messages.Count, messages.Count(m => m.Type == MessageType.ToolExecution));
            
        return result;
    }

    // SRP: UI Cleanup
    private void RemoveGeneratingMessage()
    {
        var generatingIndex = messages.FindLastIndex(m => m.Type == MessageType.Generating);
        if (generatingIndex >= 0)
        {
            messages.RemoveAt(generatingIndex);
        }
    }

    // SRP: Result Processing Strategy Pattern
    private async Task ProcessConversationResult(ConversationResult result, string userPrompt)
    {
        if (result.IsSuccess)
        {
            // Remove the last AI reasoning message to avoid duplication
            // since it will be shown in the CodeSuccessDisplay component with better formatting
            if (!string.IsNullOrEmpty(result.GeneratedCode))
            {
                var lastReasoningMessage = messages.LastOrDefault(m => m.Type == MessageType.Text && m.Role == "AI");
                if (lastReasoningMessage != null)
                {
                    // Check if the reasoning message contains similar content to the final result
                    if (TextSimilarityService.AreSimilar(lastReasoningMessage.Content, result.GeneratedCode, similarityThreshold: 0.6))
                    {
                        messages.Remove(lastReasoningMessage);
                        logger.LogInformation("Removed duplicate AI reasoning message before code result display");
                    }
                }
            }
            
            var processor = CreateResultProcessor();
            await processor.ProcessSuccessResult(result, userPrompt, selectedAgent);
        }
        else
        {
            ProcessFailureResult(result);
        }
        
        await NotifyConversationComplete(result, userPrompt);
    }

    // Factory Method Pattern: Result Processor Creation
    private IConversationResultProcessor CreateResultProcessor()
    {
        return new ConversationResultProcessor(
            messages: messages,
            pendingCodes: pendingCodes,
            codeHistory: codeHistory,
            onCodeGenerated: OnCodeGenerated,
            currentLanguage: Context.Language,
            logger: logger,
            codeExtractionService: CodeExtractionService);
    }

    // SRP: Failure Result Processing
    private void ProcessFailureResult(ConversationResult result)
    {
        var errorMessage = new ChatMessage("AI", "Code generation failed:", DateTime.Now)
        {
            Type = MessageType.CodeError,
            Content = result.ErrorMessage ?? "Unknown error",
            IsSuccess = false
        };
        
        messages.Add(errorMessage);
    }

    // SRP: External Notification
    private async Task NotifyConversationComplete(ConversationResult result, string userPrompt)
    {
        if (OnSend.HasDelegate)
        {
            var selectedProvider = GetSelectedProvider();
            var selectedModel = GetSelectedModel();
            var contextMessage = $"Using {selectedProvider?.ProviderName} - {selectedModel} with frontend tools";
            await OnSend.InvokeAsync($"{contextMessage}|{userPrompt}");
        }
    }

    // SRP: Error Handling
    private void HandleConversationError(Exception ex)
    {
        RemoveGeneratingMessage();
        
        var errorMessage = new ChatMessage("AI", "Unexpected error:", DateTime.Now)
        {
            Type = MessageType.CodeError,
            Content = ex.Message,
            IsSuccess = false
        };
        
        messages.Add(errorMessage);
        logger.LogError(ex, "Error during conversation execution");
    }

    // SRP: Cleanup
    private async Task EndConversation()
    {
        isGeneratingCode = false;
        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
    }

    // Helper Classes for Better Structure
    private record ConversationContext
    {
        public string UserPrompt { get; init; } = string.Empty;
        public AiProviderInfo Provider { get; init; } = default!;
        public string Model { get; init; } = string.Empty;
        public string ApiKey { get; init; } = string.Empty;
        public ConversationRequest ConversationRequest { get; init; } = default!;
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

    private async Task<string> GetApiKeyFromProvider(AiProviderInfo provider, string? selectedApiKeyId)
    {
        if (!string.IsNullOrEmpty(selectedApiKeyId) && provider.StoredKeys?.Any() == true)
        {
            var selectedKey = provider.StoredKeys.FirstOrDefault(k => k.Id.ToString() == selectedApiKeyId);
            if (selectedKey != null)
            {
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

    private List<Automaton.Mcp.Models.ConversationMessage> BuildMcpConversationHistory()
    {
        var conversationHistory = new List<Automaton.Mcp.Models.ConversationMessage>();
        
        foreach (var message in messages.Where(m => 
            m.Type != MessageType.System && 
            m.Type != MessageType.Generating && 
            m.Type != MessageType.ToolExecution))
        {
            string role = message.Role.ToLowerInvariant() switch
            {
                "user" => "user",
                "ai" => "assistant",
                _ => "user"
            };

            string content = message.Type == MessageType.CodeSuccess && !string.IsNullOrEmpty(message.GeneratedCode)
                ? $"{message.Content}\n\n```{Context.Language?.ToLowerInvariant() ?? "python"}\n{message.GeneratedCode}\n```"
                : message.Content;

            conversationHistory.Add(new Automaton.Mcp.Models.ConversationMessage(role, content));
        }

        return conversationHistory;
    }
    
    private int EstimateTokenCount(List<ChatMessage> messages)
    {
        return messages.Sum(m => 
            (m.Content?.Length ?? 0) + 
            (m.GeneratedCode?.Length ?? 0)) / 4;
    }

    private async Task ClearConversationHistory()
    {
        if (isGeneratingCode) return;

        if (!string.IsNullOrEmpty(_conversationId))
        {
            await McpClient.EndConversationAsync(_conversationId);
            _conversationId = null;
        }

        messages.RemoveAll(m => m.Type != MessageType.System);
        pendingCodes.Clear();

        messages.Add(new ChatMessage("System", "Conversation history cleared. AI can now see and test your code in real-time.", DateTime.Now) 
        { 
            Type = MessageType.System 
        });

        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var objRef = DotNetObjectReference.Create(this);
            await JSRuntime.InvokeVoidAsync("eval", "window.aiChatPanelInstance = null;");
            await JSRuntime.InvokeVoidAsync("setAiChatPanelRef", objRef);
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
            pendingCodes.Remove(codeId);

            messages.Add(new ChatMessage("System", "Code applied to editor", DateTime.Now) 
            { 
                Type = MessageType.System 
            });
            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();
        }
    }

    // Helper method to extract only code for editor application from AI response
    private string ExtractCodeForEditor(string response, string language)
    {
        return CodeExtractionService.ExtractCodeForEditor(response, language);
    }

    public void Dispose()
    {
        McpClient.ConversationUpdated -= OnConversationUpdated;

        if (!string.IsNullOrEmpty(_conversationId))
        {
            _ = Task.Run(() => McpClient.EndConversationAsync(_conversationId));
        }

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