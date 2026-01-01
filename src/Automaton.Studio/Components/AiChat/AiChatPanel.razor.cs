using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Automaton.Studio.Services.Mcp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Automaton.Mcp.Client;
using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging;
using Automaton.Studio.Components.AiChat.ToolResults;
using Automaton.Studio.Components.AiChat.ToolResults.Processors;
using Automaton.Studio.Components.AiChat.Models;
using Automaton.Studio.Components.AiChat.Contexts;
using Automaton.Studio.Services.Mcp.Clients;

namespace Automaton.Studio.Components.AiChat;

public partial class AiChatPanel : ComponentBase, IDisposable
{
    [Parameter] public IAiChatContext Context { get; set; } = default!;
    [Parameter] public IMcpClient McpClient { get; set; } = default!;
    [Parameter] public EventCallback<string> OnSend { get; set; }
    [Parameter] public EventCallback<string> OnCodeGenerated { get; set; }
    [Parameter] public ChatResourceType ResourceType { get; set; }
    [Parameter] public Guid ResourceId { get; set; }

    [Inject] private AiApiKeyService AiApiKeyService { get; set; } = default!;
    [Inject] private AiChatHistoryService ChatHistoryService { get; set; } = default!;
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

    // Chat history state
    private ChatSessionModel? currentSession;
    private List<ChatSessionModel> availableSessions = new();
    private bool isLoadingHistory = false;
    private bool showSessionSelector = false;
    private int lastSavedMessageCount = 0; // Track how many messages have been saved

    protected override async Task OnInitializedAsync()
    {
        // Initialize with context-specific system message
        messages.Add(new ChatMessage("System", Context.InitialSystemMessage, DateTime.Now)
        {
            Type = MessageType.System
        });

        // Subscribe to MCP client events
        McpClient.ConversationUpdated += OnConversationUpdated;

        await LoadProvidersAsync();
        await LoadChatHistoryAsync();
    }

    private async Task LoadChatHistoryAsync()
    {
        try
        {
            if (ResourceId == Guid.Empty)
            {
                logger.LogWarning("Cannot load chat history: ResourceId is empty");
                return;
            }

            isLoadingHistory = true;
            StateHasChanged();

            // Get or create active session
            currentSession = await ChatHistoryService.GetOrCreateActiveSessionAsync(
                ResourceType,
                ResourceId,
                GetSelectedProvider()?.ProviderName,
                GetSelectedModel()
            );

            if (currentSession != null)
            {
                // Load previous messages from this session
                var previousMessages = await ChatHistoryService.GetMessagesAsync(currentSession.Id);

                // Convert saved messages to ChatMessage objects and add to messages list
                foreach (var savedMsg in previousMessages)
                {
                    var chatMessage = ConvertToChatMessage(savedMsg);
                    messages.Add(chatMessage);
                }

                logger.LogInformation("Loaded {Count} messages from session {SessionId}",
                    previousMessages.Count(), currentSession.Id);

                // Track that these messages are already saved
                lastSavedMessageCount = messages.Count(m => m.Type != MessageType.System && m.Type != MessageType.Generating);

                // Load all available sessions for this resource
                availableSessions = (await ChatHistoryService.GetSessionsAsync(ResourceType, ResourceId)).ToList();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading chat history");
            // Don't show error to user, just continue without history
        }
        finally
        {
            isLoadingHistory = false;
            StateHasChanged();
        }
    }

    private ChatMessage ConvertToChatMessage(ChatMessageModel savedMsg)
    {
        var metadata = !string.IsNullOrEmpty(savedMsg.Metadata)
            ? System.Text.Json.JsonSerializer.Deserialize<ChatMessageMetadata>(savedMsg.Metadata)
            : null;

        var chatMessage = new ChatMessage(savedMsg.Role, savedMsg.Content, savedMsg.CreatedAt)
        {
            Type = (MessageType)(int)savedMsg.MessageType,
            CodeId = metadata?.CodeId,
            GeneratedCode = metadata?.GeneratedCode,
            ToolName = metadata?.ToolName,
            ToolResult = metadata?.ToolResult,
            ToolCallId = metadata?.ToolCallId,
            ToolStatus = metadata?.ToolStatus,
            ToolArguments = metadata?.ToolArguments,
            ToolError = metadata?.ToolError,
            IsReasoningCollapsed = metadata?.IsReasoningCollapsed ?? true,
            IsSuccess = metadata?.IsSuccess ?? true
        };

        return chatMessage;
    }

    private class ChatMessageMetadata
    {
        public string? CodeId { get; set; }
        public string? GeneratedCode { get; set; }
        public string? ToolName { get; set; }
        public string? ToolResult { get; set; }
        public string? ToolCallId { get; set; }
        public string? ToolStatus { get; set; }
        public string? ToolArguments { get; set; }
        public string? ToolError { get; set; }
        public bool IsReasoningCollapsed { get; set; }
        public bool IsSuccess { get; set; }
    }

    // Handle real-time conversation updates from MCP client
    private void OnConversationUpdated(object? sender, ConversationUpdatedEvent e)
    {
        if (e.ConversationId == _conversationId)
        {
            InvokeAsync(() =>
            {
                // Process AI messages (thinking, reasoning, explanations, etc.)
                if (e.Messages?.Any() == true)
                {
                    foreach (var message in e.Messages)
                    {
                        if (message.Role.ToLowerInvariant() == "assistant" && !string.IsNullOrWhiteSpace(message.Content))
                        {
                            // Handle streaming messages - update existing streaming message instead of adding new
                            if (message.IsStreaming)
                            {
                                // Find existing streaming message to update
                                var existingStreamingMsg = messages.LastOrDefault(m => 
                                    m.Role == "AI" && 
                                    m.Type == MessageType.Reasoning &&
                                    m.IsStreaming);
                                
                                if (existingStreamingMsg != null)
                                {
                                    // Update existing streaming message content
                                    var index = messages.IndexOf(existingStreamingMsg);
                                    messages[index] = existingStreamingMsg with 
                                    { 
                                        Content = message.Content,
                                        IsStreaming = true
                                    };
                                    logger.LogDebug("Updated streaming message: {Length} chars", message.Content.Length);
                                }
                                else
                                {
                                    // Create new streaming message
                                    var streamingMsg = new ChatMessage("AI", message.Content, DateTime.Now)
                                    {
                                        Type = MessageType.Reasoning,
                                        IsReasoningCollapsed = false,
                                        IsStreaming = true
                                    };
                                    messages.Add(streamingMsg);
                                    logger.LogDebug("Created new streaming message: {Length} chars", message.Content.Length);
                                }
                                
                                InvokeAsync(StateHasChanged);
                                continue;
                            }
                        
                            // For complete messages (not streaming), finalize any existing streaming message
                            var existingStreamingMessage = messages.LastOrDefault(m => 
                                m.Role == "AI" && 
                                m.Type == MessageType.Reasoning &&
                                m.IsStreaming);
                        
                            if (existingStreamingMessage != null)
                            {
                                // Finalize the streaming message with complete content
                                var index = messages.IndexOf(existingStreamingMessage);
                                messages[index] = existingStreamingMessage with 
                                { 
                                    Content = message.Content,
                                    IsStreaming = false
                                };
                                logger.LogInformation("Finalized streaming message: {Length} chars", message.Content.Length);
                                InvokeAsync(StateHasChanged);
                                continue;
                            }
                        
                            // Check if this EXACT message already exists in the conversation
                            var normalizedNewContent = message.Content.Trim();
                            var isExactDuplicate = messages.Any(m => 
                                m.Role == "AI" && 
                                (m.Type == MessageType.Text || m.Type == MessageType.Reasoning) &&
                                m.Content.Trim().Equals(normalizedNewContent, StringComparison.Ordinal));
                        
                            if (isExactDuplicate)
                            {
                                logger.LogInformation("Skipping exact duplicate AI message: {Length} chars", message.Content.Length);
                                continue;
                            }
                        
                            // Check for similar messages (near-duplicates)
                            var isSimilarDuplicate = messages.Any(m => 
                                m.Role == "AI" && 
                                (m.Type == MessageType.Text || m.Type == MessageType.Reasoning) &&
                                TextSimilarityService.AreSimilar(m.Content, message.Content, similarityThreshold: 0.95));
                        
                            if (isSimilarDuplicate)
                            {
                                logger.LogInformation("Skipping similar duplicate AI message: {Length} chars", message.Content.Length);
                                continue;
                            }
                        
                            // Determine if this is an intermediate message (during generation) or a final response
                            // Intermediate messages are shown as "Activity" (Reasoning type) for better UX
                            var isIntermediateMessage = isGeneratingCode && IsIntermediateAiMessage(message.Content);
                        
                            // Use Reasoning type for intermediate messages (collapsible, shows AI is working)
                            // Use Text type for final responses
                            var messageType = isIntermediateMessage ? MessageType.Reasoning : MessageType.Text;
                        
                            var aiMsg = new ChatMessage("AI", message.Content, DateTime.Now)
                            {
                                Type = messageType,
                                IsReasoningCollapsed = false  // Show expanded initially, will auto-collapse after generation
                            };
                            messages.Add(aiMsg);
                            logger.LogInformation("Added AI {MessageType} message: {Length} chars", messageType, message.Content.Length);
                            InvokeAsync(StateHasChanged);
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
                        var toolMessage = new ChatMessage("AI", GetToolDescription(toolCall.Name), DateTime.Now)
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

                // Add tool results - UPDATE existing executing messages or add new if needed
                foreach (var toolResult in e.ToolResults)
                {
                    // Check if we already have a completed result message for this tool call
                    var existingResultMessage = messages.FirstOrDefault(m => 
                        m.Type == MessageType.ToolExecution && 
                        m.ToolCallId == toolResult.ToolCallId &&
                        (m.ToolStatus == "success" || m.ToolStatus == "error"));

                    if (existingResultMessage != null)
                    {
                        // Already have a result message, skip
                        continue;
                    }

                    // Find the executing message to update
                    var executingMessage = messages.FirstOrDefault(m =>
                        m.Type == MessageType.ToolExecution &&
                        m.ToolCallId == toolResult.ToolCallId &&
                        m.ToolStatus == "executing");

                    var toolCallForResult = e.ToolCalls.FirstOrDefault(tc => tc.Id == toolResult.ToolCallId);
                    
                    if (executingMessage != null && Context.ProcessToolResults)
                    {
                        // Update the existing executing message with result details
                        var processedResult = _toolResultProcessorFactory.ProcessToolResult(
                            toolCallForResult?.Name ?? executingMessage.ToolName ?? "unknown", 
                            toolResult, 
                            Context.Language);

                        var index = messages.IndexOf(executingMessage);
                        messages[index] = executingMessage with 
                        { 
                            Content = FormatToolResultFromViewModel(processedResult),
                            ToolStatus = toolResult.IsSuccess ? "success" : "error",
                            ToolResult = toolResult.Result,
                            ToolError = toolResult.ErrorMessage,
                            ToolResultViewModel = processedResult,
                            IsSuccess = toolResult.IsSuccess
                        };
                        logger.LogInformation("Updated tool message with result: {ToolName} - {Status}", 
                            executingMessage.ToolName, toolResult.IsSuccess ? "Success" : "Failed");
                    }
                    else if (executingMessage != null)
                    {
                        // Just update status if not processing results
                        var index = messages.IndexOf(executingMessage);
                        messages[index] = executingMessage with 
                        { 
                            ToolStatus = toolResult.IsSuccess ? "success" : "error",
                            IsSuccess = toolResult.IsSuccess
                        };
                    }
                    else if (toolCallForResult != null && Context.ProcessToolResults)
                    {
                        // No executing message found, add a new result message
                        var processedResult = _toolResultProcessorFactory.ProcessToolResult(
                            toolCallForResult.Name, toolResult, Context.Language);

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
                        logger.LogInformation("Added tool result message: {ToolName} - {Status}", 
                            toolCallForResult.Name, toolResult.IsSuccess ? "Success" : "Failed");
                    }
                }

                StateHasChanged();
                ScrollToBottom();
            });
        }
    }
    
    /// <summary>
    /// Determine if a message is an intermediate AI message (shown during generation).
    /// Intermediate messages include reasoning, analysis, explanations between tool calls, etc.
    /// Final messages are typically the complete response with code or final answer.
    /// </summary>
    private bool IsIntermediateAiMessage(string content)
    {
        // Skip if it's very short (likely not meaningful)
        if (content.Length < 10)
            return false;
            
        // Skip if it contains ONLY code blocks (this is likely a final code response)
        var trimmedContent = content.Trim();
        if (trimmedContent.StartsWith("```") && trimmedContent.EndsWith("```"))
        {
            // Count code block markers
            var codeBlockCount = 0;
            var idx = 0;
            while ((idx = content.IndexOf("```", idx)) >= 0)
            {
                codeBlockCount++;
                idx += 3;
            }
            
            // If it's just one code block (2 markers: opening and closing)
            if (codeBlockCount == 2)
            {
                var firstCodeBlockIndex = content.IndexOf("```");
                var lastCodeBlockIndex = content.LastIndexOf("```");
                
                // Check if there's any meaningful text before or after the code block
                if (firstCodeBlockIndex >= 0 && lastCodeBlockIndex > firstCodeBlockIndex)
                {
                    var hasTextBefore = content.AsSpan(0, firstCodeBlockIndex).Trim().Length > 0;
                    var afterCodeStart = lastCodeBlockIndex + 3;
                    var hasTextAfter = afterCodeStart < content.Length && 
                                       content.AsSpan(afterCodeStart).Trim().Length > 0;
                    
                    // Pure code block = final response, not intermediate
                    if (!hasTextBefore && !hasTextAfter)
                        return false;
                }
            }
        }
        
        // Any text message during generation is considered intermediate
        // This includes AI's thoughts, explanations, analysis, etc.
        return true;
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

    private string GetToolDescription(string toolName)
    {
        return toolName switch
        {
            "get_current_code" => "Reading code",
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
                    selectedProviderModel = $"{firstAvailableProvider.ProviderName}:{firstAvailableProvider.SupportedModels.First()}";
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
        return availableProviders?.FirstOrDefault(p =>
            string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
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

        // Save user message to history
        await SaveMessageToHistoryAsync(userMessage);

        promptText = string.Empty;
        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
    }

    private async Task SaveMessageToHistoryAsync(ChatMessage message)
    {
        try
        {
            if (currentSession == null || ResourceId == Guid.Empty)
            {
                return;
            }

            // Skip system and generating messages
            if (message.Type == MessageType.System || message.Type == MessageType.Generating)
            {
                return;
            }

            var metadata = new ChatMessageMetadata
            {
                CodeId = message.CodeId,
                GeneratedCode = message.GeneratedCode,
                ToolName = message.ToolName,
                ToolResult = message.ToolResult,
                ToolCallId = message.ToolCallId,
                ToolStatus = message.ToolStatus,
                ToolArguments = message.ToolArguments,
                ToolError = message.ToolError,
                IsReasoningCollapsed = message.IsReasoningCollapsed,
                IsSuccess = message.IsSuccess
            };

            var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata);

            SaveChatMessageRequest saveRequest = new SaveChatMessageRequest { SessionId = currentSession.Id, Role = message.Role, Content = message.Content, MessageType = (ChatMessageType)(int)message.Type, Metadata = metadataJson };

            await ChatHistoryService.SaveMessageAsync(saveRequest);

            // Update the counter to reflect that this message has been saved
            lastSavedMessageCount++;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving message to history");
            // Don't throw - history saving shouldn't break the chat experience
        }
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
            Provider = provider.ProviderName,
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
            // Note: We no longer remove AI reasoning messages here.
            // The final explanation from the AI should be displayed in the chat panel
            // alongside the generated code. Only duplicate messages (detected earlier via
            // similarity check in OnConversationUpdated) are skipped.
            
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

        // Auto-collapse reasoning messages after generation completes
        CollapseReasoningMessages();

        // Save all new AI messages from this conversation to history
        await SaveConversationMessagesToHistoryAsync();

        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
    }

    private async Task SaveConversationMessagesToHistoryAsync()
    {
        try
        {
            if (currentSession == null || ResourceId == Guid.Empty)
            {
                return;
            }

            // Get all saveable messages (exclude system and generating)
            var allSaveableMessages = messages.Where(m =>
                m.Type != MessageType.System &&
                m.Type != MessageType.Generating &&
                !string.IsNullOrWhiteSpace(m.Content)
            ).ToList();

            // Only save messages we haven't saved yet
            var unsavedMessages = allSaveableMessages.Skip(lastSavedMessageCount).ToList();

            if (!unsavedMessages.Any())
            {
                logger.LogDebug("No new messages to save for session {SessionId}", currentSession.Id);
                return;
            }

            // Build list of messages to save
            var newMessages = new List<ChatMessageItem>();

            foreach (var message in unsavedMessages)
            {
                var metadata = new ChatMessageMetadata
                {
                    CodeId = message.CodeId,
                    GeneratedCode = message.GeneratedCode,
                    ToolName = message.ToolName,
                    ToolResult = message.ToolResult,
                    ToolCallId = message.ToolCallId,
                    ToolStatus = message.ToolStatus,
                    ToolArguments = message.ToolArguments,
                    ToolError = message.ToolError,
                    IsReasoningCollapsed = message.IsReasoningCollapsed,
                    IsSuccess = message.IsSuccess
                };

                newMessages.Add(new ChatMessageItem
                {
                    Role = message.Role,
                    Content = message.Content,
                    MessageType = (ChatMessageType)(int)message.Type,
                    Metadata = System.Text.Json.JsonSerializer.Serialize(metadata)
                });
            }

            logger.LogInformation("Saving {Count} new messages to history for session {SessionId}",
                newMessages.Count, currentSession.Id);

            var saveRequest = new SaveChatMessagesRequest
            {
                SessionId = currentSession.Id,
                Messages = newMessages
            };

            // Save the new messages to the backend
            await ChatHistoryService.SaveMessagesAsync(saveRequest);

            // Update the tracking counter
            lastSavedMessageCount = allSaveableMessages.Count;

            logger.LogInformation("Successfully saved {Count} messages to session {SessionId}. Total saved: {Total}",
                newMessages.Count, currentSession.Id, lastSavedMessageCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving conversation messages to history");
            // Don't throw - history saving shouldn't break the chat experience
        }
    }
    
    /// <summary>
    /// Auto-collapse all reasoning messages after AI finishes generating.
    /// This helps keep the chat clean while preserving the reasoning for reference.
    /// </summary>
    private void CollapseReasoningMessages()
    {
        // Create a new list to avoid modifying collection during iteration
        messages = messages.Select(msg =>
        {
            if (msg.Type == MessageType.Reasoning && !msg.IsReasoningCollapsed)
            {
                return msg with { IsReasoningCollapsed = true };
            }
            return msg;
        }).ToList();
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
            if (envKeys.TryGetValue(provider.ProviderName, out var envKey) && !string.IsNullOrEmpty(envKey))
            {
                return envKey;
            }
        }

        logger.LogWarning("No valid API key found for provider {ProviderName}", provider.ProviderName);
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

        // Create a new session for fresh conversation
        await CreateNewSessionAsync();

        messages.RemoveAll(m => m.Type != MessageType.System);
        pendingCodes.Clear();

        messages.Add(new ChatMessage("System", "Started new chat session. Previous conversations are saved and can be accessed from session history.", DateTime.Now)
        {
            Type = MessageType.System
        });

        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
    }

    private async Task CreateNewSessionAsync()
    {
        try
        {
            if (ResourceId == Guid.Empty)
            {
                return;
            }

            var request = new CreateChatSessionRequest
            {
                ResourceType = ResourceType,
                ResourceId = ResourceId,
                Title = $"Chat {DateTime.Now:MMM dd, HH:mm}",
                Provider = GetSelectedProvider()?.ProviderName,
                Model = GetSelectedModel()
            };

            currentSession = await ChatHistoryService.CreateSessionAsync(request);

            if (currentSession != null)
            {
                // Reset the saved message counter for the new session
                lastSavedMessageCount = 0;

                // Refresh available sessions list
                availableSessions = (await ChatHistoryService.GetSessionsAsync(ResourceType, ResourceId)).ToList();
                logger.LogInformation("Created new chat session {SessionId}", currentSession.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating new session");
        }
    }

    private async Task SwitchSessionAsync(Guid sessionId)
    {
        try
        {
            if (isGeneratingCode) return;

            // End current conversation if any
            if (!string.IsNullOrEmpty(_conversationId))
            {
                await McpClient.EndConversationAsync(_conversationId);
                _conversationId = null;
            }

            // Set this session as active
            await ChatHistoryService.SetActiveSessionAsync(sessionId);

            // Load the session and its messages
            var sessionWithMessages = await ChatHistoryService.GetSessionWithMessagesAsync(sessionId);

            if (sessionWithMessages != null)
            {
                currentSession = sessionWithMessages.Session;

                // Clear current messages (except system message)
                messages.RemoveAll(m => m.Type != MessageType.System);
                pendingCodes.Clear();

                // Load messages from the selected session
                foreach (var savedMsg in sessionWithMessages.Messages)
                {
                    var chatMessage = ConvertToChatMessage(savedMsg);
                    messages.Add(chatMessage);
                }

                // Update the counter to reflect loaded messages
                lastSavedMessageCount = messages.Count(m => m.Type != MessageType.System && m.Type != MessageType.Generating);

                // Refresh available sessions
                availableSessions = (await ChatHistoryService.GetSessionsAsync(ResourceType, ResourceId)).ToList();

                logger.LogInformation("Switched to session {SessionId} with {Count} messages",
                    sessionId, sessionWithMessages.Messages.Count);

                await InvokeAsync(StateHasChanged);
                await ScrollToBottom();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error switching session");
        }
    }

    private async Task DeleteSessionAsync(Guid sessionId)
    {
        try
        {
            var success = await ChatHistoryService.DeleteSessionAsync(sessionId);

            if (success)
            {
                // Refresh available sessions
                availableSessions = (await ChatHistoryService.GetSessionsAsync(ResourceType, ResourceId)).ToList();

                // If we deleted the current session, create a new one
                if (currentSession?.Id == sessionId)
                {
                    await ClearConversationHistory();
                }

                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting session");
        }
    }

    private void ToggleSessionSelector()
    {
        showSessionSelector = !showSessionSelector;
        StateHasChanged();
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