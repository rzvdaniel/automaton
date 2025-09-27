using Automaton.Studio.Shared.Components;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.AiApiKeys;

partial class AiApiKeysPage : ComponentBase
{
    [Inject] private AiApiKeysViewModel ViewModel { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadAiApiKeys();
    }

    private List<EnhancedEmpty.EmptyActionModel> GetAiApiKeySecondaryActions()
    {
        return new List<EnhancedEmpty.EmptyActionModel>
        {
            new() { Text = "Provider Setup Guide", Icon = "setting", OnClick = () => Task.FromResult(0) },
            new() { Text = "AI Documentation", Icon = "book", OnClick = () => Task.FromResult(0) }
        };
    }

    private List<EnhancedEmpty.FeatureCardModel> GetAiApiKeyFeatureCards()
    {
        return new List<EnhancedEmpty.FeatureCardModel>
        {
            new() 
            { 
                Title = "OpenAI Integration", 
                Description = "Connect to GPT-4, ChatGPT, and DALL-E", 
                Icon = "openai",
                IconColor = "#10a37f"
            },
            new() 
            { 
                Title = "Anthropic Claude", 
                Description = "Access powerful Claude AI models", 
                Icon = "anthropic",
                IconColor = "#d4822a"
            },
            new() 
            { 
                Title = "Google Gemini", 
                Description = "Integrate with Google's AI services", 
                Icon = "google",
                IconColor = "#4285f4"
            }
        };
    }

    private List<EnhancedEmpty.HelpLinkModel> GetAiApiKeyHelpLinks()
    {
        return new List<EnhancedEmpty.HelpLinkModel>
        {
            new() { Text = "OpenAI API Keys", Url = "https://platform.openai.com/api-keys", Icon = "link" },
            new() { Text = "Anthropic Console", Url = "https://console.anthropic.com/", Icon = "link" },
            new() { Text = "Google AI Studio", Url = "https://aistudio.google.com/app/apikey", Icon = "link" }
        };
    }
}