using AntDesign;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Apps.Components.LinkStepDialog;

public partial class LinkStepDialog : ComponentBase
{
    [CascadingParameter] public ModalRef ModalRef { get; set; } = default!;
    [Parameter] public List<LinkedStepInfo> AvailableSteps { get; set; } = new();
    [Parameter] public Guid AppId { get; set; }
    
    [Inject] private AppStepsService AppStepsService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private MessageService MessageService { get; set; } = default!;

    private string searchText = "";
    private Guid? selectedStepId;
    
    private List<LinkedStepInfo> filteredSteps => string.IsNullOrWhiteSpace(searchText)
        ? AvailableSteps
        : AvailableSteps.Where(s => 
            s.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            s.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            (s.Description?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

    private void OnSearch(string value)
    {
        searchText = value;
        StateHasChanged();
    }

    private async Task SelectStep(Guid stepId)
    {
        selectedStepId = selectedStepId == stepId ? null : stepId;
        
        // If step is selected, link it immediately
        if (selectedStepId.HasValue)
        {
            try
            {
                await AppStepsService.LinkStepAsync(AppId, selectedStepId.Value);
                _ = MessageService.SuccessAsync("Step linked successfully");
                await ModalRef.CloseAsync();
            }
            catch (Exception ex)
            {
                // Log the error for debugging
                _ = MessageService.ErrorAsync($"Failed to link step: {ex.Message}");
            }
        }
        
        StateHasChanged();
    }

    private async Task CreateNewStep()
    {
        await ModalRef.CloseAsync();
        NavigationManager.NavigateTo($"/customsteps/new?appId={AppId}");
    }

    private string GetLanguageColor(string language)
    {
        return language?.ToLower() switch
        {
            "python" => "blue",
            "batch" => "cyan",
            "powershell" => "purple",
            _ => "default"
        };
    }
}
