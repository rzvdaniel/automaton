using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Admin.AiProviders
{
    partial class AdminAiProvidersPage : ComponentBase
    {
        [Inject] private AdminAiProvidersViewModel ViewModel { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            await ViewModel.LoadProviders();
        }

        private void NavigateToModels(Guid providerId)
        {
            NavigationManager.NavigateTo($"/admin/ai-models?providerId={providerId}");
        }

        private void NavigateToCreate()
        {
            NavigationManager.NavigateTo("/admin/ai-providers/create");
        }

        private void NavigateToEdit(Guid providerId)
        {
            NavigationManager.NavigateTo($"/admin/ai-providers/edit/{providerId}");
        }
    }
}
