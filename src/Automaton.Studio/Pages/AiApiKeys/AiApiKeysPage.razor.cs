using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.AiApiKeys;

partial class AiApiKeysPage : ComponentBase
{
    [Inject] private AiApiKeysViewModel ViewModel { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadAiApiKeys();
    }
}