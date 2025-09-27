using AntDesign;
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;
using Automaton.Studio.Shared.Components;

namespace Automaton.Studio.Pages.Runners;

partial class RunnersPage : ComponentBase
{
    public bool loading;

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private RunnersViewModel RunnersViewModel { get; set; } = default!;
    [Inject] private ModalService ModalService { get; set; }
    [Inject] private MessageService MessageService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        loading = true;

        try
        {
            await RunnersViewModel.GetRunners();
        }
        catch
        {
            await MessageService.ErrorAsync(Resources.Errors.FlowsListNotLoaded);
        }
        finally
        {
            loading = false;
        }

        await base.OnInitializedAsync();
    }

    private List<string> GetRunnerActionCardBullets()
    {
        return new List<string>
        {
            "Cross-platform console application",
            "Secure API key authentication",
            "Real-time workflow execution",
            "Comprehensive logging and monitoring"
        };
    }

    private List<EnhancedEmpty.EmptyActionModel> GetRunnerSecondaryActions()
    {
        return new List<EnhancedEmpty.EmptyActionModel>
        {
            new() { Text = "Setup Guide", Icon = "setting", OnClick = () => Task.FromResult(0) }
        };
    }

    private List<EnhancedEmpty.FeatureCardModel> GetRunnerFeatureCards()
    {
        return new List<EnhancedEmpty.FeatureCardModel>
        {
            new() 
            { 
                Title = "Easily deploy and manage your Runners", 
                Description = "Deploy runners on any platform with our cross-platform console application. Connect securely with API keys and monitor execution in real-time.", 
                Icon = "play-square",
                IconColor = "#722ed1"
            },
            new() 
            { 
                Title = "Reliably scale as your demand shifts", 
                Description = "Add more runners to handle increased workload. Deploy across different environments and platforms with our distributed execution architecture.", 
                Icon = "thunderbolt",
                IconColor = "#fa8c16"
            },
            new() 
            { 
                Title = "Monitor and secure your executions", 
                Description = "Get comprehensive logging and monitoring for all workflow executions. Secure communications with encrypted API key authentication.", 
                Icon = "shield-check",
                IconColor = "#52c41a"
            }
        };
    }

    private List<EnhancedEmpty.HelpLinkModel> GetRunnerHelpLinks()
    {
        return new List<EnhancedEmpty.HelpLinkModel>
        {
            new() { Text = "Runners overview", Description = "Detailed information covering all aspects of runners.", Icon = "profile", Url = "#" },
            new() { Text = "Installation Guide", Description = "Step-by-step guide for installing and configuring runners.", Icon = "setting", Url = "#" },
            new() { Text = "Authentication Setup", Description = "Learn how to configure secure API key authentication.", Icon = "key", Url = "#" },
            new() { Text = "Troubleshooting", Description = "Common issues and solutions for runner connectivity.", Icon = "question-circle", Url = "#" }
        };
    }
}
