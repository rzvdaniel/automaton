using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Automaton.Studio.Services.Mcp;
using Automaton.Studio.Pages.Steps.Designer.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Enums;
using Automaton.Mcp.Client;
using Automaton.Mcp.Models;

namespace Automaton.Studio.Pages.Apps.Components;

public partial class WebAiChatPanel : ComponentBase, IDisposable
{
    [Parameter] public EventCallback<string> OnCodeGenerated { get; set; }
    [Parameter] public Func<Task<string>>? GetCurrentCode { get; set; }
    [Parameter] public string? CurrentWebAppId { get; set; }

    [Inject] private AiApiKeyService AiApiKeyService { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private ILogger<WebAiChatPanel> logger { get; set; } = default!;
    [Inject] private IAutomatonMcpClient McpClient { get; set; } = default!;
    [Inject] private ISystemPromptBuilder SystemPromptBuilder { get; set; } = default!;

    protected ElementReference messagesContainer;

    private string selectedProviderModel = string.Empty;
    private string promptText = string.Empty;
    private bool showNoProvidersMessage = false;
    private bool isGeneratingCode = false;
    private Dictionary<string, string> pendingCodes = new();
    private string? _conversationId;

    private List<AiProviderInfo>? availableProviders;

    private List<ChatMessage> messages = new()
    {
        new ChatMessage("System", "AI web code generation ready. I can help you create HTML, CSS, and JavaScript pages.", DateTime.Now)
        {
            Type = MessageType.System
        }
    };

    protected override async Task OnInitializedAsync()
    {
        McpClient.ConversationUpdated += OnConversationUpdated;
        await LoadProvidersAsync();
    }

    private void OnConversationUpdated(object? sender, ConversationUpdatedEvent e)
    {
        if (e.ConversationId == _conversationId)
        {
            InvokeAsync(() =>
            {
                // Process AI reasoning messages
                if (e.Messages?.Any() == true)
                {
                    foreach (var message in e.Messages)
                    {
                        if (message.Role.ToLowerInvariant() == "assistant" && !string.IsNullOrWhiteSpace(message.Content))
                        {
                            var reasoningMsg = new ChatMessage("AI", message.Content, DateTime.Now)
                            {
                                Type = MessageType.Text
                            };
                            messages.Add(reasoningMsg);
                        }
                    }
                }

                StateHasChanged();
                ScrollToBottom();
            });
        }
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
        if (!IsReadyToSendRealtime) return;

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

    private async Task BeginConversation()
    {
        isGeneratingCode = true;
        pendingCodes.Clear();

        var userMessage = new ChatMessage("User", System.Net.WebUtility.HtmlEncode(promptText), DateTime.Now);
        messages.Add(userMessage);

        var generatingMessage = new ChatMessage("AI", "Generating web page code...", DateTime.Now)
        {
            IsGenerating = true,
            Type = MessageType.Generating
        };
        messages.Add(generatingMessage);

        promptText = string.Empty;
        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
    }

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

    private ConversationRequest BuildConversationRequest(string userPrompt, AiProviderInfo provider, string model, string apiKey)
    {
        var request = new ConversationRequest
        {
            InitialMessage = userPrompt,
            SystemPrompt = SystemPromptBuilder.BuildWebAppSystemPrompt(),
            Provider = provider.Provider,
            ApiKey = apiKey,
            Model = model,
            ConversationHistory = BuildMcpConversationHistory(),
            Context = new Dictionary<string, object>
            {
                ["language"] = "html",
                ["contextId"] = CurrentWebAppId ?? Guid.NewGuid().ToString(),
                ["getCurrentCode"] = GetCurrentCode ?? (() => Task.FromResult(string.Empty))
            }
        };

        return request;
    }

    private async Task<ConversationResult> ExecuteConversation(ConversationContext context)
    {
        _conversationId = context.ConversationRequest.ConversationId;
        var result = await McpClient.StartConversationAsync(context.ConversationRequest);
        _conversationId = result.ConversationId;

        // Remove generating message
        RemoveGeneratingMessage();

        return result;
    }

    private void RemoveGeneratingMessage()
    {
        var generatingIndex = messages.FindLastIndex(m => m.Type == MessageType.Generating);
        if (generatingIndex >= 0)
        {
            messages.RemoveAt(generatingIndex);
        }
    }

    private async Task ProcessConversationResult(ConversationResult result, string userPrompt)
    {
        if (result.IsSuccess)
        {
            // Add the generated code
            var codeId = Guid.NewGuid().ToString();
            pendingCodes[codeId] = result.GeneratedCode ?? "";

            var successMessage = new ChatMessage("AI", "Web page generated successfully!", DateTime.Now)
            {
                Type = MessageType.CodeSuccess,
                CodeId = codeId,
                GeneratedCode = result.GeneratedCode,
                IsSuccess = true
            };

            messages.Add(successMessage);

            // Auto-apply the generated code
            if (OnCodeGenerated.HasDelegate && !string.IsNullOrEmpty(result.GeneratedCode))
            {
                await OnCodeGenerated.InvokeAsync(result.GeneratedCode);
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
    }

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
        logger.LogError(ex, "Error during web app conversation execution");
    }

    private async Task EndConversation()
    {
        isGeneratingCode = false;
        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
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
                ? $"{message.Content}\n\n```html\n{message.GeneratedCode}\n```"
                : message.Content;

            conversationHistory.Add(new Automaton.Mcp.Models.ConversationMessage(role, content));
        }

        return conversationHistory;
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

    private async Task ScrollToBottom()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("eval", "var element = document.getElementById('web-ai-chat-messages'); if(element) { element.scrollTop = element.scrollHeight; }");
        }
        catch
        {
            // Ignore JS interop errors
        }
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

        messages.Add(new ChatMessage("System", "Conversation history cleared. Ready to generate web pages.", DateTime.Now)
        {
            Type = MessageType.System
        });

        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();
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

    public void Dispose()
    {
        McpClient.ConversationUpdated -= OnConversationUpdated;

        if (!string.IsNullOrEmpty(_conversationId))
        {
            _ = Task.Run(() => McpClient.EndConversationAsync(_conversationId));
        }
    }

    // Helper class for better structure
    private record ConversationContext
    {
        public string UserPrompt { get; init; } = string.Empty;
        public AiProviderInfo Provider { get; init; } = default!;
        public string Model { get; init; } = string.Empty;
        public string ApiKey { get; init; } = string.Empty;
        public ConversationRequest ConversationRequest { get; init; } = default!;
    }
}
