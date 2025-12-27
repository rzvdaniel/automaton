using AntDesign;
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;
using Automaton.Studio.Shared.Components;
using Microsoft.JSInterop;

namespace Automaton.Studio.Pages.Runners;

partial class RunnersPage : ComponentBase
{
    public bool loading;
    private bool deployModalVisible;

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private RunnersViewModel RunnersViewModel { get; set; } = default!;
    [Inject] private ModalService ModalService { get; set; }
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        loading = true;

        try
        {
            await RunnersViewModel.GetRunners();
        }
        catch
        {
            _ = MessageService.ErrorAsync(Resources.Errors.FlowsListNotLoaded);
        }
        finally
        {
            loading = false;
        }

        await base.OnInitializedAsync();
    }

    private void ShowDeployRunnerModal()
    {
        deployModalVisible = true;
    }

    private async Task CopyDockerCommand()
    {
        var command = @"docker run -d \
  --name automaton-runner \
  --restart unless-stopped \
  -e AUTOMATON_API_KEY=ak_your_key_here \
  -e AUTOMATON_RUNNER_NAME=My-Runner \
  rzvdaniel/automaton-runner:latest";
        
        await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", command);
        await MessageService.SuccessAsync("Command copied to clipboard!");
    }

    private void GoToApiKeys()
    {
        deployModalVisible = false;
        NavigationManager.NavigateTo("/apikeys");
    }

    private List<string> GetRunnerActionCardBullets()
    {
        return new List<string>
        {
            "One command deployment with Docker",
            "Automatic connection to Automaton server",
            "Pre-installed Python packages for custom steps",
            "No configuration files needed"
        };
    }

    private List<EnhancedEmpty.EmptyActionModel> GetRunnerSecondaryActions()
    {
        return new List<EnhancedEmpty.EmptyActionModel>
        {
            new() { Text = "View Documentation", Icon = "book", OnClick = () => { NavigationManager.NavigateTo("/docs"); return Task.CompletedTask; } }
        };
    }

    private List<EnhancedEmpty.FeatureCardModel> GetRunnerFeatureCards()
    {
        return new List<EnhancedEmpty.FeatureCardModel>
        {
            new() 
            { 
                Title = "Deploy in seconds with Docker", 
                Description = "Just run a single command to deploy a runner. No downloads, no installers, no configuration files. Docker handles everything.", 
                Icon = "cloud-server",
                IconColor = "#1890ff"
            },
            new() 
            { 
                Title = "Scale as your demand shifts", 
                Description = "Add more runners to handle increased workload. Deploy across different environments and platforms with our distributed execution architecture.", 
                Icon = "thunderbolt",
                IconColor = "#fa8c16"
            },
            new() 
            { 
                Title = "Python ready out of the box", 
                Description = "The Docker image includes Python 3.13 and popular packages pre-installed. Your custom Python steps work immediately without setup.", 
                Icon = "code",
                IconColor = "#52c41a"
            }
        };
    }

    private List<EnhancedEmpty.HelpLinkModel> GetRunnerHelpLinks()
    {
        return new List<EnhancedEmpty.HelpLinkModel>
        {
            new() { Text = "Docker Runner Guide", Description = "Complete guide for deploying runners with Docker.", Icon = "container", Url = "/docs/#/runners/docker-runner" },
            new() { Text = "API Key Setup", Description = "How to create and manage API keys for runners.", Icon = "key", Url = "/apikeys" },
            new() { Text = "Troubleshooting", Description = "Common issues and solutions for runner connectivity.", Icon = "question-circle", Url = "/docs/#/runners/troubleshooting" }
        };
    }

    private async Task DeleteRunner(Guid id)
    {
        try
        {
            await RunnersViewModel.DeleteRunner(id);
            _ = MessageService.SuccessAsync("Runner deleted successfully");
        }
        catch
        {
            _ = MessageService.ErrorAsync("Failed to delete runner");
        }
    }
}
