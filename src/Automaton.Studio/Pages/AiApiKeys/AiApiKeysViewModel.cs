using AntDesign;
using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.AiApiKeys;

public class AiApiKeysViewModel
{
    private readonly AiApiKeyService aiApiKeyService;
    private readonly MessageService messageService;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly ProjectContextService projectContextService;

    public bool Loading { get; set; } = false;
    public bool CreateModalVisible { get; set; } = false;
    public bool CreateLoading { get; set; } = false;
    
    public IEnumerable<AiApiKeyModel> AiApiKeys { get; set; } = new List<AiApiKeyModel>();
    public CreateAiApiKeyRequest CreateRequest { get; set; } = new();

    /// <summary>
    /// Check if user can manage AI API keys (always true for personal keys).
    /// </summary>
    public bool CanManageAiApiKeys => authorizationService.CanManageResources();

    public AiApiKeysViewModel(
        AiApiKeyService aiApiKeyService, 
        MessageService messageService,
        ResourceAuthorizationService authorizationService,
        ProjectContextService projectContextService)
    {
        this.aiApiKeyService = aiApiKeyService;
        this.messageService = messageService;
        this.authorizationService = authorizationService;
        this.projectContextService = projectContextService;
    }

    public async Task LoadAiApiKeys()
    {
        Loading = true;
        try
        {
            var projectId = projectContextService.GetCurrentProjectId();
            AiApiKeys = await aiApiKeyService.GetAiApiKeysAsync(projectId);
        }
        catch (Exception ex)
        {
            await messageService.ErrorAsync($"Failed to load AI API keys: {ex.Message}");
        }
        finally
        {
            Loading = false;
        }
    }

    public void ShowCreateModal()
    {
        CreateRequest = new CreateAiApiKeyRequest();
        CreateModalVisible = true;
    }

    public async Task CreateAiApiKey()
    {
        CreateLoading = true;
        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(CreateRequest?.Name))
            {
                await messageService.ErrorAsync("Name is required");
                return;
            }
            
            if (string.IsNullOrWhiteSpace(CreateRequest?.ApiKey))
            {
                await messageService.ErrorAsync("API Key is required");
                return;
            }

            var newKey = await aiApiKeyService.CreateAiApiKeyAsync(CreateRequest);
            
            if (newKey != null)
            {
                var keysList = AiApiKeys.ToList();
                keysList.Add(newKey);
                AiApiKeys = keysList;
                
                CreateModalVisible = false;
                await messageService.SuccessAsync("AI API key added successfully!");
            }
            else
            {
                await messageService.ErrorAsync("Failed to create AI API key - returned null");
            }
        }
        catch (Exception ex)
        {
            await messageService.ErrorAsync($"Failed to create AI API key: {ex.Message}");
        }
        finally
        {
            CreateLoading = false;
        }
    }

    public async Task DeleteAiApiKey(Guid id)
    {
        try
        {
            var success = await aiApiKeyService.DeleteAiApiKeyAsync(id);
            if (success)
            {
                var keysList = AiApiKeys.ToList();
                keysList.RemoveAll(k => k.Id == id);
                AiApiKeys = keysList;
                
                await messageService.SuccessAsync("AI API key deleted successfully!");
            }
            else
            {
                await messageService.ErrorAsync("Failed to delete AI API key");
            }
        }
        catch (Exception ex)
        {
            await messageService.ErrorAsync($"Failed to delete AI API key: {ex.Message}");
        }
    }
}