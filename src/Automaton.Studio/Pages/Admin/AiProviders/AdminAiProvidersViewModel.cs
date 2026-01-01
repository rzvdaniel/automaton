using AntDesign;
using Automaton.Studio.Models.Admin;
using Automaton.Studio.Services.Admin;

namespace Automaton.Studio.Pages.Admin.AiProviders;

public class AdminAiProvidersViewModel
{
    private readonly AdminAiProviderService adminAiProviderService;
    private readonly MessageService messageService;

    public bool Loading { get; set; } = false;
    public bool CreateModalVisible { get; set; } = false;
    public bool EditModalVisible { get; set; } = false;
    public bool CreateLoading { get; set; } = false;
    public bool EditLoading { get; set; } = false;

    public IEnumerable<AiProviderAdminModel> Providers { get; set; } = new List<AiProviderAdminModel>();
    public AdminCreateAiProviderRequest CreateRequest { get; set; } = new();
    public AdminUpdateAiProviderRequest EditRequest { get; set; } = new();
    public AiProviderAdminModel? SelectedProvider { get; set; }

    public AdminAiProvidersViewModel(
        AdminAiProviderService adminAiProviderService,
        MessageService messageService)
    {
        this.adminAiProviderService = adminAiProviderService;
        this.messageService = messageService;
    }

    public async Task LoadProviders()
    {
        Loading = true;
        try
        {
            Providers = await adminAiProviderService.GetProvidersAsync();
        }
        catch (Exception ex)
        {
            _ = messageService.ErrorAsync($"Failed to load AI providers: {ex.Message}");
        }
        finally
        {
            Loading = false;
        }
    }

    public void ShowCreateModal()
    {
        CreateRequest = new AdminCreateAiProviderRequest
        {
            IsActive = true,
            RequiresApiKey = true,
            SortOrder = 0
        };
        CreateModalVisible = true;
    }

    public void ShowEditModal(AiProviderAdminModel provider)
    {
        SelectedProvider = provider;
        EditRequest = new AdminUpdateAiProviderRequest
        {
            Name = provider.Name,
            DisplayName = provider.DisplayName,
            Description = provider.Description,
            BaseUrl = provider.BaseUrl,
            IsActive = provider.IsActive,
            RequiresApiKey = provider.RequiresApiKey,
            EnvironmentKeyName = provider.EnvironmentKeyName,
            ApiKeyHelpUrl = provider.ApiKeyHelpUrl,
            IconName = provider.IconName,
            SortOrder = provider.SortOrder
        };
        EditModalVisible = true;
    }

    public async Task CreateProvider()
    {
        CreateLoading = true;
        try
        {
            if (string.IsNullOrWhiteSpace(CreateRequest?.Name))
            {
                _ = messageService.ErrorAsync("Name is required");
                return;
            }

            if (string.IsNullOrWhiteSpace(CreateRequest?.DisplayName))
            {
                _ = messageService.ErrorAsync("Display Name is required");
                return;
            }

            var newProvider = await adminAiProviderService.CreateProviderAsync(CreateRequest);

            if (newProvider != null)
            {
                var list = Providers.ToList();
                list.Add(newProvider);
                Providers = list.OrderBy(p => p.SortOrder).ThenBy(p => p.Name);

                CreateModalVisible = false;
                _ = messageService.SuccessAsync("AI provider created successfully!");
            }
            else
            {
                _ = messageService.ErrorAsync("Failed to create AI provider");
            }
        }
        catch (Exception ex)
        {
            _ = messageService.ErrorAsync($"Failed to create AI provider: {ex.Message}");
        }
        finally
        {
            CreateLoading = false;
        }
    }

    public async Task UpdateProvider()
    {
        if (SelectedProvider == null) return;

        EditLoading = true;
        try
        {
            var success = await adminAiProviderService.UpdateProviderAsync(SelectedProvider.Id, EditRequest);

            if (success)
            {
                // Reload to get updated data
                await LoadProviders();
                EditModalVisible = false;
                _ = messageService.SuccessAsync("AI provider updated successfully!");
            }
            else
            {
                _ = messageService.ErrorAsync("Failed to update AI provider");
            }
        }
        catch (Exception ex)
        {
            _ = messageService.ErrorAsync($"Failed to update AI provider: {ex.Message}");
        }
        finally
        {
            EditLoading = false;
        }
    }

    public async Task DeleteProvider(Guid id)
    {
        try
        {
            var success = await adminAiProviderService.DeleteProviderAsync(id);
            if (success)
            {
                var list = Providers.ToList();
                list.RemoveAll(p => p.Id == id);
                Providers = list;

                _ = messageService.SuccessAsync("AI provider deleted successfully!");
            }
            else
            {
                _ = messageService.ErrorAsync("Failed to delete AI provider. It may have API keys associated with it.");
            }
        }
        catch (Exception ex)
        {
            _ = messageService.ErrorAsync($"Failed to delete AI provider: {ex.Message}");
        }
    }

    public async Task ToggleProviderStatus(AiProviderAdminModel provider)
    {
        try
        {
            var request = new AdminUpdateAiProviderRequest
            {
                IsActive = !provider.IsActive
            };

            var success = await adminAiProviderService.UpdateProviderAsync(provider.Id, request);

            if (success)
            {
                provider.IsActive = !provider.IsActive;
                var status = provider.IsActive ? "activated" : "deactivated";
                _ = messageService.SuccessAsync($"AI provider {status} successfully!");
            }
            else
            {
                _ = messageService.ErrorAsync("Failed to update provider status");
            }
        }
        catch (Exception ex)
        {
            _ = messageService.ErrorAsync($"Failed to update provider status: {ex.Message}");
        }
    }
}
