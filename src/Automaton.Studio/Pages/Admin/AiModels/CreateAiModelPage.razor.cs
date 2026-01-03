using AntDesign;
using Automaton.Studio.Models.Admin;
using Automaton.Studio.Services.Admin;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Admin.AiModels
{
    partial class CreateAiModelPage : ComponentBase
    {
        [Inject] private AdminAiProviderService AdminAiProviderService { get; set; } = default!;
        [Inject] private MessageService MessageService { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        [SupplyParameterFromQuery(Name = "providerId")]
        public string? ProviderIdQuery { get; set; }

        private AiModelFormModel formModel = new();
        private IEnumerable<AiProviderAdminModel> providers = new List<AiProviderAdminModel>();
        private Guid selectedProviderId = Guid.Empty;
        private bool loading = true;
        private bool isSaving = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadProviders();
        }

        private async Task LoadProviders()
        {
            loading = true;
            try
            {
                providers = await AdminAiProviderService.GetProvidersAsync();

                // If providerId is passed in query string, pre-select it
                if (!string.IsNullOrEmpty(ProviderIdQuery) && Guid.TryParse(ProviderIdQuery, out var providerId))
                {
                    selectedProviderId = providerId;
                }
                else
                {
                    // Default to first provider
                    selectedProviderId = providers.FirstOrDefault()?.Id ?? Guid.Empty;
                }

                // Initialize with defaults
                formModel = new AiModelFormModel
                {
                    IsActive = true,
                    IsDefault = false,
                    MaxTokens = 4096,
                    SupportsStreaming = true,
                    SupportsTools = false,
                    SupportsVision = false,
                    SortOrder = 0
                };
            }
            catch (Exception ex)
            {
                _ = MessageService.ErrorAsync($"Failed to load providers: {ex.Message}");
            }
            finally
            {
                loading = false;
            }
        }

        private async Task HandleCreate()
        {
            isSaving = true;
            try
            {
                if (selectedProviderId == Guid.Empty)
                {
                    _ = MessageService.ErrorAsync("Please select a provider");
                    return;
                }

                if (string.IsNullOrWhiteSpace(formModel.ModelId))
                {
                    _ = MessageService.ErrorAsync("Model ID is required");
                    return;
                }

                if (string.IsNullOrWhiteSpace(formModel.DisplayName))
                {
                    _ = MessageService.ErrorAsync("Display Name is required");
                    return;
                }

                var request = formModel.ToCreateRequest();
                var newModel = await AdminAiProviderService.CreateModelAsync(selectedProviderId, request);

                if (newModel != null)
                {
                    _ = MessageService.SuccessAsync("AI model created successfully!");
                    NavigationManager.NavigateTo("/admin/ai-models");
                }
                else
                {
                    _ = MessageService.ErrorAsync("Failed to create AI model");
                }
            }
            catch (Exception ex)
            {
                _ = MessageService.ErrorAsync($"Failed to create AI model: {ex.Message}");
            }
            finally
            {
                isSaving = false;
            }
        }

        private void HandleCancel()
        {
            NavigationManager.NavigateTo("/admin/ai-models");
        }
    }
}
