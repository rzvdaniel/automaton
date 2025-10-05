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
            new() { Text = "Provider Setup Guide", Icon = "setting", OnClick = () => Task.FromResult(0) }
        };
    }

    private List<string> GetAiApiKeyActionCardBullets()
    {
        return new List<string>
        {
            "Connect to OpenAI, Anthropic, Google",
            "Text generation and image analysis",
            "Intelligent decision making",
            "Cost tracking and usage analytics"
        };
    }

    private List<EnhancedEmpty.FeatureCardModel> GetAiApiKeyFeatureCards()
    {
        return new List<EnhancedEmpty.FeatureCardModel>
        {
            new() 
            { 
                Title = "Easily connect to leading AI providers", 
                Description = "Integrate with OpenAI, Anthropic, Google, services. Our unified interface makes it simple to manage multiple AI providers.", 
                Icon = "robot",
                IconColor = "#722ed1"
            },
            new() 
            { 
                Title = "Intelligent automation at scale", 
                Description = "Enhance your workflows with AI capabilities like text processing, image analysis, and decision making. Scale across multiple models and providers.", 
                Icon = "thunderbolt",
                IconColor = "#fa8c16"
            },
            new() 
            { 
                Title = "Secure and monitor AI usage", 
                Description = "Track AI API usage with detailed analytics and cost monitoring. Secure your keys and monitor performance across all your AI integrations.", 
                Icon = "safety-certificate",
                IconColor = "#52c41a"
            }
        };
    }

    private List<EnhancedEmpty.HelpLinkModel> GetAiApiKeyHelpLinks()
    {
        return new List<EnhancedEmpty.HelpLinkModel>
        {
            new() { Text = "OpenAI API Keys", Url = "https://platform.openai.com/api-keys", Icon = "openai", Description = "Get your OpenAI API key for GPT and DALL-E access." },
            new() { Text = "Anthropic Console", Url = "https://console.anthropic.com/", Icon = "anthropic", Description = "Access Anthropic's Claude AI models and API keys." },
            new() { Text = "Google AI Studio", Url = "https://aistudio.google.com/app/apikey", Icon = "google", Description = "Generate API keys for Google's Gemini AI models." },
            new() { Text = "AI Integration Guide", Url = "#", Icon = "robot", Description = "Learn how to integrate AI capabilities into your workflows." }
        };
    }
}