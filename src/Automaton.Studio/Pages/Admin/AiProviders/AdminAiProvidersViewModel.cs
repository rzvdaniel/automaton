using AntDesign;
using Automaton.Studio.Models.Admin;
using Automaton.Studio.Services.Admin;

namespace Automaton.Studio.Pages.Admin.AiProviders;

public class AdminAiProvidersViewModel
{
    private readonly AdminAiProviderService adminAiProviderService;
    private readonly MessageService messageService;

    public bool Loading { get; set; } = false;

    public IEnumerable<AiProviderAdminModel> Providers { get; set; } = new List<AiProviderAdminModel>();

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
