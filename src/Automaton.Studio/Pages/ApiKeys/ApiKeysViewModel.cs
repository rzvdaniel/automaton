using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.JSInterop;
using AntDesign;

namespace Automaton.Studio.Pages.ApiKeys;

public class ApiKeysViewModel
{
    private readonly IApiKeysService apiKeysService;
    private readonly MessageService messageService;
    private readonly IJSRuntime jsRuntime;

    public bool Loading { get; set; } = true;
    public List<ApiKeyResponse>? ApiKeys { get; set; }
    
    public bool CreateModalVisible { get; set; } = false;
    public bool CreateLoading { get; set; } = false;
    public CreateApiKeyRequest CreateRequest { get; set; } = new() { Name = "", Scope = ApiKeyScope.Runner };

    public bool SuccessModalVisible { get; set; } = false;
    public string NewApiKey { get; set; } = "";

    public ApiKeysViewModel(
        IApiKeysService apiKeysService,
        MessageService messageService,
        IJSRuntime jsRuntime)
    {
        this.apiKeysService = apiKeysService;
        this.messageService = messageService;
        this.jsRuntime = jsRuntime;
    }

    public async Task LoadApiKeys()
    {
        Loading = true;
        try
        {
            ApiKeys = await apiKeysService.GetApiKeysAsync();
        }
        catch (Exception ex)
        {
            await messageService.ErrorAsync($"Failed to load API keys: {ex.Message}");
        }
        finally
        {
            Loading = false;
        }
    }

    public void ShowCreateModal()
    {
        CreateRequest = new CreateApiKeyRequest { Name = "", Scope = ApiKeyScope.Runner };
        CreateModalVisible = true;
    }

    public async Task CreateApiKey()
    {
        if (string.IsNullOrWhiteSpace(CreateRequest.Name))
        {
            await messageService.WarningAsync("Please enter a name for the API key");
            return;
        }

        CreateLoading = true;
        try
        {
            var result = await apiKeysService.CreateApiKeyAsync(CreateRequest);
            
            NewApiKey = result.ApiKey;
            CreateModalVisible = false;
            SuccessModalVisible = true;
            await LoadApiKeys(); // Refresh the list
        }
        catch (Exception ex)
        {
            await messageService.ErrorAsync($"Error creating API key: {ex.Message}");
        }
        finally
        {
            CreateLoading = false;
        }
    }

    public async Task RevokeApiKey(Guid apiKeyId)
    {
        try
        {
            var success = await apiKeysService.RevokeApiKeyAsync(apiKeyId);
            if (success)
            {
                await messageService.SuccessAsync("API key revoked successfully");
                await LoadApiKeys(); // Refresh the list
            }
            else
            {
                await messageService.ErrorAsync("Failed to revoke API key");
            }
        }
        catch (Exception ex)
        {
            await messageService.ErrorAsync($"Error revoking API key: {ex.Message}");
        }
    }

    public async Task CopyToClipboard()
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", NewApiKey);
            await messageService.SuccessAsync("API key copied to clipboard");
        }
        catch
        {
            await messageService.WarningAsync("Could not copy to clipboard. Please copy manually.");
        }
    }

    public void CloseSuccessModal()
    {
        SuccessModalVisible = false;
        NewApiKey = "";
    }

    public static string GetScopeColor(ApiKeyScope scope)
    {
        return scope switch
        {
            ApiKeyScope.Runner => "blue",
            ApiKeyScope.Execute => "green",
            ApiKeyScope.ReadOnly => "orange",
            _ => "default"
        };
    }

    public static string GetScopeClass(ApiKeyScope scope)
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