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
            new() { Text = "Runner Setup Guide", Icon = "setting", OnClick = () => Task.FromResult(0) }
        };
    }

    private List<string> GetApiKeyActionCardBullets()
    {
        return new List<string>
        {
            "Authenticate console runners",
            "Connect external applications",
            "Configurable access scopes",
            "Usage monitoring and analytics"
        };
    }

    private List<EnhancedEmpty.FeatureCardModel> GetApiKeyFeatureCards()
    {
        return new List<EnhancedEmpty.FeatureCardModel>
        {
            new() 
            { 
                Title = "Easily authenticate and manage your Runners", 
                Description = "Use API keys to securely connect console runners and desktop applications. Our scoped authentication system makes it simple to manage access.", 
                Icon = "desktop",
                IconColor = "#722ed1"
            },
            new() 
            { 
                Title = "Reliably integrate external services", 
                Description = "Connect third-party applications and CI/CD pipelines. Secure API access with configurable scopes and expiration dates for enhanced security.", 
                Icon = "api",
                IconColor = "#fa8c16"
            },
            new() 
            { 
                Title = "Monitor usage and secure your infrastructure", 
                Description = "Track API key usage with detailed logging and analytics. Revoke keys instantly and monitor access patterns for enhanced security.", 
                Icon = "safety-certificate",
                IconColor = "#52c41a"
            }
        };
    }

    private List<EnhancedEmpty.HelpLinkModel> GetApiKeyHelpLinks()
    {
        return new List<EnhancedEmpty.HelpLinkModel>
        {
            new() { Text = "API Keys overview", Url = "#", Icon = "key", Description = "Detailed information covering all aspects of API authentication." },
            new() { Text = "Authenticating your Runners", Url = "#", Icon = "desktop", Description = "A guide about how to set up and authenticate console runners." },
            new() { Text = "Security Best Practices", Url = "#", Icon = "safety", Description = "Learn how to securely manage and rotate your API keys." },
            new() { Text = "Authentication API Docs", Url = "#", Icon = "api", Description = "Use the API to programmatically manage authentication keys." }
        };
    }
}