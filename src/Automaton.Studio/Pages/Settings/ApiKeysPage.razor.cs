using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using AntDesign;

namespace Automaton.Studio.Pages.Settings;

public partial class ApiKeysPage
{
    private bool loading = true;
    private List<ApiKeyResponse>? apiKeys;
    
    private bool createModalVisible = false;
    private bool createLoading = false;
    private CreateApiKeyRequest createRequest = new() { Name = "", Scope = ApiKeyScope.Runner };

    private bool successModalVisible = false;
    private string newApiKey = "";

    [Inject] public ApiKeysService ApiKeysService { get; set; } = null!;
    [Inject] public MessageService MessageService { get; set; } = null!;
    [Inject] public IJSRuntime JSRuntime { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await LoadApiKeys();
    }

    private async Task LoadApiKeys()
    {
        loading = true;
        try
        {
            apiKeys = await ApiKeysService.GetApiKeysAsync();
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"Failed to load API keys: {ex.Message}");
        }
        finally
        {
            loading = false;
        }
    }

    private void ShowCreateModal()
    {
        createRequest = new CreateApiKeyRequest { Name = "", Scope = ApiKeyScope.Runner };
        createModalVisible = true;
    }

    private async Task CreateApiKey()
    {
        if (string.IsNullOrWhiteSpace(createRequest.Name))
        {
            await MessageService.WarningAsync("Please enter a name for the API key");
            return;
        }

        createLoading = true;
        try
        {
            var result = await ApiKeysService.CreateApiKeyAsync(createRequest);
            
            newApiKey = result.ApiKey;
            createModalVisible = false;
            successModalVisible = true;
            await LoadApiKeys(); // Refresh the list
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"Error creating API key: {ex.Message}");
        }
        finally
        {
            createLoading = false;
        }
    }

    private async Task RevokeApiKey(Guid apiKeyId)
    {
        try
        {
            var success = await ApiKeysService.RevokeApiKeyAsync(apiKeyId);
            if (success)
            {
                await MessageService.SuccessAsync("API key revoked successfully");
                await LoadApiKeys(); // Refresh the list
            }
            else
            {
                await MessageService.ErrorAsync("Failed to revoke API key");
            }
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"Error revoking API key: {ex.Message}");
        }
    }

    private async Task CopyToClipboard()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", newApiKey);
            await MessageService.SuccessAsync("API key copied to clipboard");
        }
        catch
        {
            await MessageService.WarningAsync("Could not copy to clipboard. Please copy manually.");
        }
    }

    private void CloseSuccessModal()
    {
        successModalVisible = false;
        newApiKey = "";
    }

    private static string GetScopeColor(ApiKeyScope scope)
    {
        return scope switch
        {
            ApiKeyScope.Runner => "blue",
            ApiKeyScope.Execute => "green",
            ApiKeyScope.ReadOnly => "orange",
            _ => "default"
        };
    }

    private static string GetScopeClass(ApiKeyScope scope)
    {
        return scope switch
        {
            ApiKeyScope.Runner => "scope-runner",
            ApiKeyScope.Execute => "scope-execute",
            ApiKeyScope.ReadOnly => "scope-readonly",
            _ => ""
        };
    }
}