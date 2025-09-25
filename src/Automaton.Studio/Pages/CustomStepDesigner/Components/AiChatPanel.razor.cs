using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.CustomStepDesigner.Components
{
    public partial class AiChatPanel : ComponentBase
    {
        [Parameter] public EventCallback<string> OnSend { get; set; }
        [Inject] private AiApiKeyService AiApiKeyService { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        protected ElementReference messagesContainer;

        private string selectedAgent = "DefaultAgent";
        private string selectedProviderModel = string.Empty;
        private string selectedApiKeyId = string.Empty;
        private string promptText = string.Empty;
        private bool showNoProvidersMessage = false;

        private List<AiProviderInfo>? availableProviders;

        private List<ChatMessage> messages = new()
        {
            new ChatMessage("System", "AI chat panel ready. Select a provider and model to start chatting.", DateTime.Now)
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

                        // Auto-select first API key if available
                        if (firstAvailableProvider.StoredKeys?.Any() == true)
                        {
                            selectedApiKeyId = firstAvailableProvider.StoredKeys.First().Id.ToString();
                        }
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

        private bool IsReadyToSend
        {
            get
            {
                if (string.IsNullOrWhiteSpace(promptText) || string.IsNullOrEmpty(selectedProviderModel))
                    return false;

                var selectedProvider = GetSelectedProvider();
                if (selectedProvider == null)
                    return false;

                // If provider has stored keys, we need one selected
                if (selectedProvider.StoredKeys?.Any() == true && string.IsNullOrEmpty(selectedApiKeyId))
                    return false;

                return selectedProvider.HasStoredKeys || selectedProvider.HasEnvironmentKey;
            }
        }

        private void AttachFile()
        {
            // placeholder for attach action
        }

        private async Task SendPrompt()
        {
            if (!IsReadyToSend) return;

            messages.Add(new ChatMessage("User", System.Net.WebUtility.HtmlEncode(promptText), DateTime.Now));
            var toSend = promptText;
            promptText = string.Empty;

            var selectedProvider = GetSelectedProvider();
            var selectedModel = GetSelectedModel();

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

            // Placeholder for actual send - emit event so page can handle
            if (OnSend.HasDelegate)
            {
                await OnSend.InvokeAsync($"{contextMessage}|{toSend}");
            }

            // temporarily add a mocked assistant response
            messages.Add(new ChatMessage("AI", $"<p><em>{contextMessage}</em></p><p>Generating response... (mock response)</p>", DateTime.Now));

            await InvokeAsync(StateHasChanged);

            // scroll to bottom (best-effort)
            try
            {
                await JSRuntime.InvokeVoidAsync("blazorHelpers.scrollToBottom", messagesContainer);
            }
            catch { }
        }

        private record ChatMessage(string Role, string Content, DateTime Time);

        private const string Agent = "Agent";
        private const string Assistant = "Assistant";
    }
}
