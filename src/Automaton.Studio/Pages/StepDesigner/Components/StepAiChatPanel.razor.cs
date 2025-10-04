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
    [Parameter] public Func<string, Task<ExecutionResult>>? ExecuteCode { get; set; }
    [Parameter] public string? CurrentStepId { get; set; }

    [Inject] private AiApiKeyService AiApiKeyService { get; set; } = default!;
    [Inject] private AiStepGenerationService AiStepGenerationService { get; set; } = default!;
    [Inject] private IAutomatonMcpClient McpClient { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private ILogger<StepAiChatPanel> logger { get; set; } = default!;

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
                // Process tool calls and results
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

    // Helper method to count lines in code
    private int GetCodeLineCount(string code)
    {
        if (string.IsNullOrEmpty(code)) return 0;
        return code.Split('\n', StringSplitOptions.None).Length;
    }

    // Helper method to get full code from JSON result (not truncated)
    private string GetFullCodeFromResult(string toolResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            if (doc.RootElement.TryGetProperty("code", out var codeElement))
            {
                return codeElement.GetString() ?? string.Empty;
            }
        }
        catch
        {
            if (toolResult.Contains("```"))
            {
                var start = toolResult.IndexOf("```");
                var end = toolResult.IndexOf("```", start + 3);
                if (end > start)
                {
                    var codeBlock = toolResult.Substring(start + 3, end - start - 3);
                    var firstNewline = codeBlock.IndexOf('\n');
                    if (firstNewline > 0 && firstNewline < 20)
                    {
                        codeBlock = codeBlock.Substring(firstNewline + 1);
                    }
                    return codeBlock.Trim();
                }
            }
        }
        
        return string.Empty;
    }

    private string GetCodePreview(string toolResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            if (doc.RootElement.TryGetProperty("code", out var codeElement))
            {
                var code = codeElement.GetString() ?? "";
                var lines = code.Split('\n');
                if (lines.Length <= 5 && code.Length <= 200)
                {
                    return code;
                }
                else if (lines.Length > 5)
                {
                    return string.Join('\n', lines.Take(5)) + "\n# ... (truncated)";
                }
                else
                {
                    return code.Length > 200 ? code[..197] + "..." : code;
                }
            }
        }
        catch
        {
            if (toolResult.Contains("```"))
            {
                var start = toolResult.IndexOf("```");
                var end = toolResult.IndexOf("```", start + 3);
                if (end > start)
                {
                    var codeBlock = toolResult.Substring(start + 3, end - start - 3);
                    var firstNewline = codeBlock.IndexOf('\n');
                    if (firstNewline > 0 && firstNewline < 20)
                    {
                        codeBlock = codeBlock.Substring(firstNewline + 1);
                    }
                    return codeBlock.Trim();
                }
            }
        }
        
        return string.Empty;
    }

    private string GetExecutionSummary(string toolResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            var root = doc.RootElement;
            
            var success = root.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
            var output = root.TryGetProperty("output", out var outputElement) ? outputElement.GetString() : "";
            var executionTime = root.TryGetProperty("executionTime", out var timeElement) ? timeElement.GetString() : "";
            
            var summary = success ? "✅ **Success**" : "❌ **Failed**";
            
            if (!string.IsNullOrEmpty(executionTime))
            {
                summary += $" (in {executionTime})";
            }
            
            if (!string.IsNullOrEmpty(output) && output.Length < 200)
            {
                summary += $"\n```\n{output}\n```";
            }
            else if (!string.IsNullOrEmpty(output))
            {
                summary += $"\n```\n{output[..197]}...\n```";
            }
            
            if (root.TryGetProperty("outputVariables", out var varsElement))
            {
                var varsJson = varsElement.GetRawText();
                if (varsJson != "{}" && varsJson != "null")
                {
                    summary += $"\n\n**Output Variables:** {varsJson}";
                }
            }
            
            return summary;
        }
        catch
        {
            return toolResult.Length > 200 ? toolResult[..197] + "..." : toolResult;
        }
    }

    private string GetVariableSummary(string toolResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            var root = doc.RootElement;
            var summary = "";
            
            if (root.TryGetProperty("inputVariables", out var inputVars) && inputVars.ValueKind == JsonValueKind.Array)
            {
                var inputCount = inputVars.GetArrayLength();
                summary += $"📥 **{inputCount} Input Variable{(inputCount != 1 ? "s" : "")}**";
                
                foreach (var variable in inputVars.EnumerateArray().Take(3))
                {
                    if (variable.TryGetProperty("name", out var nameEl) && 
                        variable.TryGetProperty("type", out var typeEl))
                    {
                        summary += $"\n• `{nameEl.GetString()}` ({typeEl.GetString()})";
                    }
                }
                
                if (inputCount > 3)
                {
                    summary += $"\n• ... and {inputCount - 3} more";
                }
            }
            
            if (root.TryGetProperty("outputVariables", out var outputVars) && outputVars.ValueKind == JsonValueKind.Array)
            {
                var outputCount = outputVars.GetArrayLength();
                if (!string.IsNullOrEmpty(summary)) summary += "\n\n";
                summary += $"📤 **{outputCount} Output Variable{(outputCount != 1 ? "s" : "")}**";
                
                foreach (var variable in outputVars.EnumerateArray().Take(3))
                {
                    if (variable.TryGetProperty("name", out var nameEl) && 
                        variable.TryGetProperty("type", out var typeEl))
                    {
                        summary += $"\n• `{nameEl.GetString()}` ({typeEl.GetString()})";
                    }
                }
                
                if (outputCount > 3)
                {
                    summary += $"\n• ... and {outputCount - 3} more";
                }
            }
            
            return summary;
        }
        catch
        {
            return "Variables analyzed";
        }
    }

    private string GetToolStatusClass(string? status)
    {
        return status switch
        {
            "executing" => "status-executing",
            "success" => "status-success",
            "error" => "status-error",
            _ => "status-unknown"
        };
    }

    // Proper Unicode emoji status icons
    private string GetToolStatusIcon(string? status)
    {
        return status switch
        {
            "executing" => "⏳",  // Hourglass emoji
            "success" => "✅",    // Check mark emoji
            "error" => "❌",      // Cross mark emoji
            _ => "🔧"             // Wrench emoji
        };
    }

    private string GetToolStatusText(string? status)
    {
        return status switch
        {
            "executing" => "Executing",
            "success" => "Success",
            "error" => "Failed",
            _ => "Unknown"
        };
    }

    private string FormatToolContentForDisplay(string content)
    {
        var html = content
            .Replace("**", "<strong>", StringComparison.Ordinal)
            .Replace("**", "</strong>", StringComparison.Ordinal)
            .Replace("`", "<code>", StringComparison.Ordinal)
            .Replace("`", "</code>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);

        // Handle code blocks
        if (html.Contains("```"))
        {
            html = System.Text.RegularExpressions.Regex.Replace(html, 
                @"```(\w+)?<br>(.*?)```", 
                "<div class=\"code-block\"><pre><code>$2</code></pre></div>",
                System.Text.RegularExpressions.RegexOptions.Singleline);
        }

        // Handle collapsible details
        if (html.Contains("<details"))
        {
            html = html
                .Replace("<details", "<details", StringComparison.Ordinal)
                .Replace("</details>", "</details>", StringComparison.Ordinal)
                .Replace("<summary", "<summary", StringComparison.Ordinal)
                .Replace("</summary>", "</summary>", StringComparison.Ordinal);
        }

        return html;
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
                if (selectedAgent == Ask)
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
                    pendingCodes[codeId] = result.GeneratedCode ?? string.Empty;
                    
                    codeHistory.Add(new CodeVersion(
                        result.GeneratedCode ?? string.Empty, 
                        DateTime.Now, 
                        userPrompt.Length > 50 ? userPrompt[..47] + "..." : userPrompt,
                        false));
                }
                else
                {
                    var codeMessage = new ChatMessage("AI", "Code generated and applied with frontend tools:", DateTime.Now)
                    {
                        Type = MessageType.CodeSuccess,
                        GeneratedCode = result.GeneratedCode,
                        IsSuccess = true
                    };
                    
                    messages.Add(codeMessage);

                    if (OnCodeGenerated.HasDelegate && !string.IsNullOrEmpty(result.GeneratedCode))
                    {
                        await OnCodeGenerated.InvokeAsync(result.GeneratedCode);
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

    private List<ChatMessage> FilterConversationHistory()
    {
        const int maxMessages = 20;
        const int maxTokens = 8000;
        
        var allMessages = messages.Where(m => 
            m.Type != MessageType.System && 
            m.Type != MessageType.Generating).ToList();
            
        if (allMessages.Count <= maxMessages)
        {
            return allMessages;
        }
        
        var recentMessages = allMessages.TakeLast(maxMessages).ToList();
        var estimatedTokens = EstimateTokenCount(recentMessages);
        
        while (estimatedTokens > maxTokens && recentMessages.Count > 4)
        {
            var oldestNonTool = recentMessages.FirstOrDefault(m => m.Type != MessageType.ToolExecution);
            if (oldestNonTool != null)
            {
                recentMessages.Remove(oldestNonTool);
            }
            else
            {
                recentMessages.RemoveAt(0);
            }
            estimatedTokens = EstimateTokenCount(recentMessages);
        }
        
        return recentMessages;
    }
    
    private bool ShouldIncludeCodeInHistory(ChatMessage message)
    {
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
        public ToolResultViewModel? ToolResultViewModel { get; init; }  // NEW: For component rendering
    }

    private record CodeVersion(string Code, DateTime Timestamp, string Description, bool WasApplied);

    private const string Agent = "Agent";
    private const string Ask = "Ask";

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