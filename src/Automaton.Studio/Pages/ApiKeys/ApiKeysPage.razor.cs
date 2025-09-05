using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.ApiKeys;

public partial class ApiKeysPage
{
    [Inject] public ApiKeysViewModel ViewModel { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadApiKeys();
    }
}