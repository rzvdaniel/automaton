using Microsoft.AspNetCore.Components;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.Apps.Components.PublishAppDialog;

partial class PublishAppDialog
{
    protected override async Task OnInitializedAsync()
    {
        // Generate initial slug from display name if PublicUrl is empty
        if (string.IsNullOrEmpty(Options.PublicUrl) && !string.IsNullOrEmpty(Options.DisplayName))
        {
            Options.PublicUrl = AppsService.GenerateSlugPreview(Options.DisplayName);
        }

        await base.OnInitializedAsync();
    }

    private void OnSlugInput(ChangeEventArgs e)
    {
        var input = e.Value?.ToString() ?? string.Empty;

        // Clean the input on the fly
        var cleaned = AppsService.GenerateSlugPreview(input);
        Options.PublicUrl = cleaned;

        StateHasChanged();
    }
}
