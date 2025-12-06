using AntDesign;
using Automaton.Studio.Helpers;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Apps.Components.LinkStepDialog;

public partial class LinkStepDialog : FeedbackComponent<LinkStepDialogModel>
{
    [Inject] private AppStepsService AppStepsService { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private MessageService MessageService { get; set; } = default!;

    private string searchText = "";
    
    private List<LinkedStepInfo> filteredSteps => string.IsNullOrWhiteSpace(searchText)
        ? (Options?.AvailableSteps ?? new List<LinkedStepInfo>())
        : (Options?.AvailableSteps ?? new List<LinkedStepInfo>()).Where(s => 
            s.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            s.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            (s.Description?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

    private void OnSearch(string value)
    {
        searchText = value;
        StateHasChanged();
    }

    private void SelectStep(Guid stepId)
    {
        if (Options == null) return;
        
        // Toggle selection
        if (Options.SelectedStepId == stepId)
        {
            Options.SelectedStepId = null;
        }
        else
        {
            Options.SelectedStepId = stepId;
        }
        
        StateHasChanged();
    }

    private void CreateNewStep()
    {
        if (Options != null)
        {
            NavigationManager.NavigateTo($"/customsteps/new?appId={Options.AppId}");
        }
    }

    private string GetLanguageColor(string language) => StepHelpers.GetLanguageColor(language);
}
