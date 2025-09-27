using Automaton.Studio.Shared.Components;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.ApiKeys;

public partial class ApiKeysPage
{
    [Inject] public ApiKeysViewModel ViewModel { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadApiKeys();
    }

    private List<EnhancedEmpty.EmptyActionModel> GetApiKeySecondaryActions()
    {
        return new List<EnhancedEmpty.EmptyActionModel>
        {
            new() { Text = "Runner Setup Guide", Icon = "setting", OnClick = () => Task.FromResult(0) },
            new() { Text = "View Documentation", Icon = "book", OnClick = () => Task.FromResult(0) }
        };
    }

    private List<EnhancedEmpty.FeatureCardModel> GetApiKeyFeatureCards()
    {
        return new List<EnhancedEmpty.FeatureCardModel>
        {
            new() 
            { 
                Title = "Console Runners", 
                Description = "Authenticate desktop and server runners", 
                Icon = "desktop",
                IconColor = "#52c41a"
            },
            new() 
            { 
                Title = "External Integration", 
                Description = "Connect third-party applications and services", 
                Icon = "api",
                IconColor = "#1890ff"
            },
            new() 
            { 
                Title = "Scoped Access", 
                Description = "Control permissions with different scope levels", 
                Icon = "safety-certificate",
                IconColor = "#722ed1"
            }
        };
    }

    private List<EnhancedEmpty.HelpLinkModel> GetApiKeyHelpLinks()
    {
        return new List<EnhancedEmpty.HelpLinkModel>
        {
            new() { Text = "Authentication Guide", Url = "#", Icon = "safety" },
            new() { Text = "Scope Permissions", Url = "#", Icon = "team" },
            new() { Text = "Runner Setup", Url = "#", Icon = "desktop" }
        };
    }
}