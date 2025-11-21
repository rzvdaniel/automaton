using Microsoft.AspNetCore.Components;
using AntDesign;
using Automaton.Studio.Models;

namespace Automaton.Studio.Pages.Admin.AI;

public partial class AIProvidersPage : ComponentBase
{
    [Inject] private AIProvidersViewModel ViewModel { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadProviders();
    }

    private void ShowCreateProviderModal()
    {
        ViewModel.ShowCreateProviderModal();
    }

    private void ShowEditProviderModal(AiProviderConfigModel provider)
    {
        ViewModel.ShowEditProviderModal(provider);
    }

    private async Task SaveProvider()
    {
        await ViewModel.SaveProvider();
    }

    private async Task DeleteProvider(Guid providerId)
    {
        await ViewModel.DeleteProvider(providerId);
    }

    private void ShowCreateModelModal(Guid providerId)
    {
        ViewModel.ShowCreateModelModal(providerId);
    }

    private void ShowEditModelModal(Guid providerId, AiModelConfigModel model)
    {
        ViewModel.ShowEditModelModal(providerId, model);
    }

    private async Task SaveModel()
    {
        await ViewModel.SaveModel();
    }

    private async Task DeleteModel(Guid providerId, Guid modelId)
    {
        await ViewModel.DeleteModel(providerId, modelId);
    }
}
