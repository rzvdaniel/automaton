using Automaton.Studio.Models.Admin;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Admin.AiModels
{
    partial class AdminAiModelsPage : ComponentBase
    {
        [Inject] private AdminAiModelsViewModel ViewModel { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        [SupplyParameterFromQuery(Name = "providerId")]
        public string? ProviderIdQuery { get; set; }

        protected override async Task OnInitializedAsync()
        {
            Guid? providerId = null;
            if (!string.IsNullOrEmpty(ProviderIdQuery) && Guid.TryParse(ProviderIdQuery, out var id))
            {
                providerId = id;
            }
            await ViewModel.LoadModels(providerId);
        }

        private async Task OnProviderFilterChanged(AiProviderAdminModel? provider)
        {
            await ViewModel.LoadModels(provider?.Id);
        }

        private void NavigateToCreate()
        {
            var queryString = !string.IsNullOrEmpty(ProviderIdQuery) ? $"?providerId={ProviderIdQuery}" : "";
            NavigationManager.NavigateTo($"/admin/ai-models/create{queryString}");
        }

        private void NavigateToEdit(Guid modelId)
        {
            NavigationManager.NavigateTo($"/admin/ai-models/edit/{modelId}");
        }
    }
}
