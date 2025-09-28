using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Automaton.Studio.Pages.CustomStepDesigner.Components;

public partial class AiChatPanel : ComponentBase, IDisposable
{
    [Parameter] public EventCallback<string> OnSend { get; set; }
    [Parameter] public EventCallback<string> OnCodeGenerated { get; set; }
    [Parameter] public string CurrentLanguage { get; set; } = "Python";
    [Parameter] public List<VariableInfo>? InputVariables { get; set; }
    [Parameter] public List<VariableInfo>? OutputVariables { get; set; }

    [Inject] private AiApiKeyService AiApiKeyService { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    protected ElementReference messagesContainer;

    private string selectedAgent = Ask;
    private string selectedProviderModel = string.Empty;
    private string promptText = string.Empty;
    private bool showNoProvidersMessage = false;
    private bool isGeneratingCode = false;
    private Dictionary<string, string> pendingCodes = new();

    private List<AiProviderInfo>? availableProviders;

    private List<ChatMessage> messages = new()
    {
        new ChatMessage("System", "AI code panel ready. Select a provider and model to start generating code.", DateTime.Now)
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
            messages.Add(new ChatMessage("System", $"Error loading AI providers: {ex.Message}", DateTime.Now));
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
            messages.Add(new ChatMessage("AI", $"<p><em>{contextMessage}</em></p><p>?? Generating code...</p>", DateTime.Now));
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
                OutputVariables = OutputVariables?.ToList()
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
                    var formattedCode = $"<div style='background: #f6f8fa; padding: 12px; border-radius: 6px; border-left: 4px solid #28a745;'>" +
                                      $"<strong>✅ Code generated successfully:</strong><br/><br/>" +
                                      $"<pre style='background: #fff; padding: 8px; border-radius: 4px; overflow-x: auto; margin-bottom: 12px;'><code>{System.Net.WebUtility.HtmlEncode(response.GeneratedCode)}</code></pre>" +
                                      $"<button onclick='applyCode(\"{codeId}\")' style='background: #1890ff; color: white; border: none; padding: 8px 16px; border-radius: 4px; cursor: pointer; font-size: 12px;'>Apply Code</button>" +
                                      $"</div>";

                    messages.Add(new ChatMessage("AI", formattedCode, DateTime.Now));

                    // Store the code for later application with unique ID
                    pendingCodes[codeId] = response.GeneratedCode;
                }
                else
                {
                    // Agent mode: Show code and auto-apply (existing behavior)
                    var formattedCode = $"<div style='background: #f6f8fa; padding: 12px; border-radius: 6px; border-left: 4px solid #28a745;'>" +
                                      $"<strong>✅ Code generated and applied:</strong><br/><br/>" +
                                      $"<pre style='background: #fff; padding: 8px; border-radius: 4px; overflow-x: auto;'><code>{System.Net.WebUtility.HtmlEncode(response.GeneratedCode)}</code></pre>" +
                                      $"</div>";

                    messages.Add(new ChatMessage("AI", formattedCode, DateTime.Now));

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
                var errorMessage = $"<div style='background: #f8f9fa; padding: 12px; border-radius: 6px; border-left: 4px solid #dc3545;'>" +
                                  $"<strong>? Code generation failed:</strong><br/>" +
                                  $"<span style='color: #dc3545;'>{System.Net.WebUtility.HtmlEncode(response.ErrorMessage)}</span>" +
                                  $"</div>";

                messages.Add(new ChatMessage("AI", errorMessage, DateTime.Now));
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
            if (messages.LastOrDefault()?.Content?.Contains("?? Generating code") == true)
            {
                messages.RemoveAt(messages.Count - 1);
            }

            var errorMessage = $"<div style='background: #f8f9fa; padding: 12px; border-radius: 6px; border-left: 4px solid #dc3545;'>" +
                              $"<strong>? Unexpected error:</strong><br/>" +
                              $"<span style='color: #dc3545;'>{System.Net.WebUtility.HtmlEncode(ex.Message)}</span>" +
                              $"</div>";

            messages.Add(new ChatMessage("AI", errorMessage, DateTime.Now));
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

    private record ChatMessage(string Role, string Content, DateTime Time);

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
        if (pendingCodes.TryGetValue(codeId, out var code) && OnCodeGenerated.HasDelegate)
        {
            await OnCodeGenerated.InvokeAsync(code);

            // Remove the applied code from pending
            pendingCodes.Remove(codeId);

            // Add confirmation message
            messages.Add(new ChatMessage("System", "✅ Code applied to editor", DateTime.Now));
            await InvokeAsync(StateHasChanged);
            await ScrollToBottom();
        }
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
