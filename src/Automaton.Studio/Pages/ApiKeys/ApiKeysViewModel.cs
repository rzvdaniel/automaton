using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.JSInterop;
using AntDesign;
using Automaton.Core.Enums;

namespace Automaton.Studio.Pages.ApiKeys;

public class ApiKeysViewModel
{
    private readonly IApiKeysService apiKeysService;
    private readonly MessageService messageService;
    private readonly IJSRuntime jsRuntime;
    private readonly ResourceAuthorizationService authorizationService;

    public bool Loading { get; set; } = true;
    public List<ApiKeyResponse>? ApiKeys { get; set; }
    
    public bool CreateModalVisible { get; set; } = false;
    public bool CreateLoading { get; set; } = false;
    public CreateApiKeyRequest CreateRequest { get; set; } = new() { Name = "", Scope = ApiKeyScope.Runner };

    public bool SuccessModalVisible { get; set; } = false;
    public string NewApiKey { get; set; } = "";
    public ApiKeyScope NewApiKeyScope { get; set; } = ApiKeyScope.FullAccess;

    /// <summary>
    /// Check if user can create API keys.
    /// In Personal Mode: always true
    /// In Project Mode: only ProjectAdmin and Developer
    /// </summary>
    public bool CanCreateApiKey => authorizationService.CanCreateApiKey();

    /// <summary>
    /// Check if user can manage API keys.
    /// In Personal Mode: always true for owned keys
    /// In Project Mode: only ProjectAdmin and Developer
    /// </summary>
    public bool CanManageApiKeys => authorizationService.CanManageResources();

    public ApiKeysViewModel(
        IApiKeysService apiKeysService,
        MessageService messageService,
        IJSRuntime jsRuntime,
        ResourceAuthorizationService authorizationService)
    {
        this.apiKeysService = apiKeysService;
        this.messageService = messageService;
        this.jsRuntime = jsRuntime;
        this.authorizationService = authorizationService;
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
        CreateRequest = new CreateApiKeyRequest { Name = "", Scope = ApiKeyScope.FullAccess };
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
            NewApiKeyScope = result.Scope; // Store the scope for usage instructions
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
        NewApiKeyScope = ApiKeyScope.FullAccess;
    }

    public static string GetScopeColor(ApiKeyScope scope)
    {
        return scope switch
        {
            ApiKeyScope.ReadOnly => "orange",
            ApiKeyScope.Execute => "green", 
            ApiKeyScope.Runner => "blue",
            ApiKeyScope.WebApp => "cyan",
            ApiKeyScope.FullAccess => "purple",
            _ => "default"
        };
    }

    public static string GetScopeClass(ApiKeyScope scope)
    {
        return scope switch
        {
            ApiKeyScope.ReadOnly => "scope-readonly",
            ApiKeyScope.Execute => "scope-execute",
            ApiKeyScope.Runner => "scope-runner",
            ApiKeyScope.WebApp => "scope-webapp",
            ApiKeyScope.FullAccess => "scope-fullaccess",
            _ => ""
        };
    }

    public string GetUsageInstructions()
    {
        return NewApiKeyScope switch
        {
            ApiKeyScope.Runner or ApiKeyScope.Execute or ApiKeyScope.FullAccess =>
$@"Use this API key to authenticate console runners and execute workflows:
automaton-runner auth --api-key {NewApiKey}
export AUTOMATON_API_KEY=""{NewApiKey}""",

            ApiKeyScope.WebApp =>
                "This API key enables your published web apps to execute flows securely. The key is used automatically by the server when users interact with your web apps - you don't need to configure it in your web app code.",

            ApiKeyScope.ReadOnly =>
                "This API key provides read-only access to flows and execution history. Use it for monitoring, reporting, or integration with external dashboards.",

            _ => "API key created successfully."
        };
    }
}