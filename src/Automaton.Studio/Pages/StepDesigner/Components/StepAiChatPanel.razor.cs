using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Automaton.Mcp.Client;
using Automaton.Mcp.Client.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Processors;
using Automaton.Studio.Pages.StepDesigner.Components.ToolResults;
using Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Utilities;

namespace Automaton.Studio.Pages.StepDesigner.Components;

public partial class StepAiChatPanel : ComponentBase, IDisposable
{
    [Parameter] public EventCallback<string> OnSend { get; set; }
    [Parameter] public EventCallback<string> OnCodeGenerated { get; set; }
    [Parameter] public string CurrentLanguage { get; set; } = "Python";
    [Parameter] public List<VariableInfo>? InputVariables { get; set; }
    [Parameter] public List<VariableInfo>? OutputVariables { get; set; }
    [Parameter] public Func<Task<string>>? GetCurrentCode { get; set; }
    [Parameter] public Func<string, Task<ExecutionResult>>? ExecuteCode { get; set; }
    [Parameter] public string? CurrentStepId { get; set; }

    [Inject] private AiApiKeyService AiApiKeyService { get; set; } = default!;
    [Inject] private AiStepGenerationService AiStepGenerationService { get; set; } = default!;
    [Inject] private IAutomatonMcpClient McpClient { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private ILogger<StepAiChatPanel> logger { get; set; } = default!;

    private record ChatMessage(string Role, string Content, DateTime Time)
    {
        public bool IsGenerating { get; init; } = false;
        public string? CodeId { get; init; }
        public bool IsSuccess { get; init; } = true;
        public string? GeneratedCode { get; init; }
        public MessageType Type { get; init; } = MessageType.Text;
        public string? ToolName { get; init; }
        public string? ToolResult { get; init; }
        public string? ToolCallId { get; init; }
        public string? ToolStatus { get; init; }
        public string? ToolArguments { get; init; }
        public string? ToolError { get; init; }
        public ToolResultViewModel? ToolResultViewModel { get; init; }
    }

    private record CodeVersion(string Code, DateTime Timestamp, string Description, bool WasApplied);

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

    private List<ChatMessage> messages = new()
    {
        new ChatMessage("System", "AI code panel ready with MCP integration. AI can now see and test your code in real-time.", DateTime.Now) 
        { 
            Type = MessageType.System 
        }
    };

    protected override async Task OnInitializedAsync()
    {
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

                // Update tool results - but preserve the message in the list!
                foreach (var toolResult in e.ToolResults)
                {
                    var toolMessage = messages.FirstOrDefault(m => 
                        m.Type == MessageType.ToolExecution && 
                        m.ToolCallId == toolResult.ToolCallId);

                    if (toolMessage != null)
                    {
                        // Process tool result using the new architecture
                        var processedResult = _toolResultProcessorFactory.ProcessToolResult(
                            toolMessage.ToolName!, toolResult, CurrentLanguage);

                        var updatedMessage = toolMessage with
                        {
                            Content = FormatToolResultFromViewModel(processedResult),
                            IsSuccess = toolResult.IsSuccess,
                            ToolStatus = toolResult.IsSuccess ? "success" : "error",
                            ToolResult = toolResult.Result,
                            ToolError = toolResult.ErrorMessage,
                            Time = DateTime.Now,
                            ToolResultViewModel = processedResult  // Store for component rendering
                        };

                        var index = messages.IndexOf(toolMessage);
                        messages[index] = updatedMessage;
                        
                        logger.LogInformation("Updated tool message: {ToolName} - {Status} (preserved at index {Index})", 
                            toolMessage.ToolName, toolResult.IsSuccess ? "Success" : "Failed", index);
                    }
                    else
                    {
                        var toolCallForResult = e.ToolCalls.FirstOrDefault(tc => tc.Id == toolResult.ToolCallId);
                        if (toolCallForResult != null)
                        {
                            // Process tool result using the new architecture
                            var processedResult = _toolResultProcessorFactory.ProcessToolResult(
                                toolCallForResult.Name, toolResult, CurrentLanguage);

                            var resultMessage = new ChatMessage("AI", FormatToolResultFromViewModel(processedResult), DateTime.Now)
                            {
                                Type = MessageType.ToolExecution,
                                ToolCallId = toolResult.ToolCallId,
                                ToolName = toolCallForResult.Name,
                                ToolStatus = toolResult.IsSuccess ? "success" : "error",
                                ToolResult = toolResult.Result,
                                ToolError = toolResult.ErrorMessage,
                                ToolResultViewModel = processedResult  // Store for component rendering
                            };
                            
                            messages.Add(resultMessage);
                            logger.LogInformation("Added standalone tool message: {ToolName} - {Status}", toolCallForResult.Name, toolResult.IsSuccess ? "Success" : "Failed");
                        }
                    }
                }

                StateHasChanged();
                ScrollToBottom();
            });
        }
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

    // Helper method to detect if response contains actual code
    private bool IsActualCode(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        // Check if the response contains code patterns
        var codeIndicators = new[]
        {
            "def ", "class ", "import ", "from ", // Python
            "function ", "const ", "let ", "var ", // JavaScript
            "public ", "private ", "void ", "int ", "string ", // C#
            "if ", "for ", "while ", "try ", "catch ", // General programming constructs
            "{", "}", "[", "]", "(", ")", "=", ";", // Code symbols
        };

        // Check for actual code structure (not just explanatory text)
        var hasCodeStructure = codeIndicators.Any(indicator => 
            response.Contains(indicator, StringComparison.OrdinalIgnoreCase));

        // Also check if it's mostly explanatory text
        var explanatoryPhrases = new[]
        {
            "the persistent failure",
            "suggests that there might be",
            "this could prevent",
            "as a workaround",
            "you can try",
            "if you have further details",
            "those could help"
        };

        var hasExplanatoryText = explanatoryPhrases.Any(phrase => 
            response.Contains(phrase, StringComparison.OrdinalIgnoreCase));

        // If it has explanatory text and no clear code structure, it's probably not code
        if (hasExplanatoryText && !hasCodeStructure)
            return false;

        // Check for code block markers
        if (response.Contains("```") || response.Contains("def ") || response.Contains("import "))
            return true;

        // Check if it has multiple programming keywords
        var keywordCount = codeIndicators.Count(indicator => 
            response.Contains(indicator, StringComparison.OrdinalIgnoreCase));

        return keywordCount >= 3; // Require at least 3 programming keywords
    }

    // Proper Unicode emoji icons - these work in browsers
    private string GetToolIcon(string toolName)
    {
        return toolName switch
        {
            "get_current_code" => "📖",    // Book emoji
            "execute_code" => "🧪",        // Test tube emoji  
            "get_step_variables" => "🔍",  // Magnifying glass emoji
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
            isGeneratingCode = true;
            pendingCodes.Clear();

            messages.Add(new ChatMessage("User", System.Net.WebUtility.HtmlEncode(promptText), DateTime.Now));
            var userPrompt = promptText;
            promptText = string.Empty;

            var selectedProvider = GetSelectedProvider();
            var selectedModel = GetSelectedModel();
            var selectedApiKeyId = GetSelectedApiKeyId();

            var contextMessage = $"Using {selectedProvider?.ProviderName} - {selectedModel} with frontend tools";
            if (!string.IsNullOrEmpty(selectedApiKeyId) && selectedProvider?.StoredKeys?.Any() == true)
            {
                var selectedKey = selectedProvider.StoredKeys.FirstOrDefault(k => k.Id.ToString() == selectedApiKeyId);
                if (selectedKey != null)
                {
                    contextMessage += $" (Key: {selectedKey.Name})";
                }
            }

            var generatingMessage = new ChatMessage("AI", contextMessage, DateTime.Now) 
            { 
                IsGenerating = true, 
                Type = MessageType.Generating 
            };
            messages.Add(generatingMessage);
            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();

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
                    ["currentStepId"] = CurrentStepId ?? Guid.NewGuid().ToString(),
                    ["getCurrentCode"] = GetCurrentCode ?? (() => Task.FromResult(string.Empty)),
                    ["executeCode"] = ExecuteCode ?? ((code) => Task.FromResult(new ExecutionResult 
                    { 
                        IsSuccess = false, 
                        ErrorMessage = "Execution not available" 
                    }))
                }
            };

            _conversationId = conversationRequest.ConversationId;
            var result = await McpClient.StartConversationAsync(conversationRequest);
            _conversationId = result.ConversationId;

            // Remove the generating message - BUT preserve tool execution messages!
            var generatingIndex = messages.FindLastIndex(m => m.Type == MessageType.Generating);
            if (generatingIndex >= 0)
            {
                messages.RemoveAt(generatingIndex);
            }

            logger.LogInformation("Messages after removing generating message: {Count}, Tool messages: {ToolCount}", 
                messages.Count, 
                messages.Count(m => m.Type == MessageType.ToolExecution));

            if (result.IsSuccess)
            {
                // Check if the result contains actual code or just explanatory text
                bool isCode = IsActualCode(result.GeneratedCode);
                
                if (selectedAgent == Ask)
                {
                    if (isCode && !string.IsNullOrEmpty(result.GeneratedCode))
                    {
                        var codeId = Guid.NewGuid().ToString();
                        var codeMessage = new ChatMessage("AI", "Code generated successfully with frontend tools:", DateTime.Now)
                        {
                            Type = MessageType.CodeSuccess,
                            CodeId = codeId,
                            GeneratedCode = result.GeneratedCode,
                            IsSuccess = true
                        };
                        
                        messages.Add(codeMessage);
                        
                        // Store only the code part for potential application
                        var codeForEditor = ExtractCodeForEditor(result.GeneratedCode, CurrentLanguage);
                        pendingCodes[codeId] = codeForEditor;
                        
                        codeHistory.Add(new CodeVersion(
                            codeForEditor, 
                            DateTime.Now, 
                            userPrompt.Length > 50 ? userPrompt[..47] + "..." : userPrompt,
                            false));
                    }
                    else
                    {
                        // Just add as a regular AI response message, not as code
                        var responseMessage = new ChatMessage("AI", result.GeneratedCode ?? "No response received.", DateTime.Now)
                        {
                            Type = MessageType.Text,
                            IsSuccess = true
                        };
                        
                        messages.Add(responseMessage);
                    }
                }
                else
                {
                    if (isCode && !string.IsNullOrEmpty(result.GeneratedCode))
                    {
                        var codeMessage = new ChatMessage("AI", "Code generated and applied with frontend tools:", DateTime.Now)
                        {
                            Type = MessageType.CodeSuccess,
                            GeneratedCode = result.GeneratedCode,
                            IsSuccess = true
                        };
                        
                        messages.Add(codeMessage);

                        // Extract only the code part for editor application
                        var codeForEditor = ExtractCodeForEditor(result.GeneratedCode, CurrentLanguage);
                        if (OnCodeGenerated.HasDelegate && !string.IsNullOrEmpty(codeForEditor))
                        {
                            await OnCodeGenerated.InvokeAsync(codeForEditor);
                        }
                    }
                    else
                    {
                        // Just add as a regular AI response message, don't apply to editor
                        var responseMessage = new ChatMessage("AI", result.GeneratedCode ?? "No response received.", DateTime.Now)
                        {
                            Type = MessageType.Text,
                            IsSuccess = true
                        };
                        
                        messages.Add(responseMessage);
                    }
                }
            }
            else
            {
                var errorMessage = new ChatMessage("AI", "Code generation failed:", DateTime.Now)
                {
                    Type = MessageType.CodeError,
                    Content = result.ErrorMessage ?? "Unknown error",
                    IsSuccess = false
                };
                
                messages.Add(errorMessage);
            }

            if (OnSend.HasDelegate)
            {
                await OnSend.InvokeAsync($"{contextMessage}|{userPrompt}");
            }
        }
        catch (Exception ex)
        {
            var generatingIndex = messages.FindLastIndex(m => m.Type == MessageType.Generating);
            if (generatingIndex >= 0)
            {
                messages.RemoveAt(generatingIndex);
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

    private List<Automaton.Mcp.Client.Models.ConversationMessage> BuildMcpConversationHistory()
    {
        var conversationHistory = new List<Automaton.Mcp.Client.Models.ConversationMessage>();
        
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
        return CodeExtractionUtility.ExtractCodeForEditor(response, language);
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