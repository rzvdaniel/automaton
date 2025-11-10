using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Automaton.Studio.Pages.Steps.Designer.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Automaton.Mcp.Client;
using Automaton.Mcp.Client.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Pages.Apps.Components;

public partial class WebAiChatPanel : ComponentBase, IDisposable
{
    [Parameter] public EventCallback<string> OnCodeGenerated { get; set; }
    [Parameter] public Func<Task<string>>? GetCurrentCode { get; set; }
    [Parameter] public string? CurrentWebAppId { get; set; }

    [Inject] private AiApiKeyService AiApiKeyService { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private ILogger<WebAiChatPanel> logger { get; set; } = default!;

    protected ElementReference messagesContainer;

    private string selectedProviderModel = string.Empty;
    private string promptText = string.Empty;
    private bool showNoProvidersMessage = false;
    private bool isGeneratingCode = false;
    private Dictionary<string, string> pendingCodes = new();

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
        await LoadProvidersAsync();
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

        var userPrompt = promptText;
        promptText = string.Empty;
        await InvokeAsync(StateHasChanged);
        await ScrollToBottom();

        try
        {
            // TODO: Implement actual AI generation
            // For now, add a placeholder message
            messages.RemoveAll(m => m.Type == MessageType.Generating);

            var responseMessage = new ChatMessage("AI", "AI generation will be implemented soon. This will generate complete HTML/CSS/JavaScript web pages.", DateTime.Now)
            {
                Type = MessageType.Text
            };
            messages.Add(responseMessage);

            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();
        }
        catch (Exception ex)
        {
            messages.RemoveAll(m => m.Type == MessageType.Generating);

            var errorMessage = new ChatMessage("AI", "Error generating code:", DateTime.Now)
            {
                Type = MessageType.CodeError,
                Content = ex.Message,
                IsSuccess = false
            };

            messages.Add(errorMessage);
            logger.LogError(ex, "Error during web code generation");
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
        // Cleanup if needed
    }
}
