using AntDesign;
using Automaton.Core.Enums;
using Automaton.Studio.Pages.Flows.Components.NewFlow;
using Automaton.Studio.Pages.Flows.Components.RenameFlow;
using Automaton.Studio.Shared.Components;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Flows
{
    partial class FlowsPage : ComponentBase
    {
        public bool loading;

        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private FlowsViewModel FlowsViewModel { get; set; } = default!;
        [Inject] private ModalService ModalService { get; set; }
        [Inject] private MessageService MessageService { get; set; }

        protected override async Task OnInitializedAsync()
        {
            loading = true;

            try
            {
                await FlowsViewModel.GetFlows();
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

        private async Task RunFlow(FlowModel flow)
        {
            var result = await FlowsViewModel.RunFlow(flow.Id, flow.RunnerIds);

            await MessageService.InfoAsync(result);
        }

        private void EditFlow(Guid id, string name)
        {
            NavigationManager.NavigateTo($"flow/designer/{id}/{name}");
        }

        private async Task DeleteFlow(Guid id)
        {
            await FlowsViewModel.DeleteFlow(id);

            StateHasChanged();
        }

        private async Task RenameFlow(Guid id, string currentName)
        {
            var renameFlowModel = new RenameFlowModel
            {
                Id = id,
                Name = currentName
            };

            var modalRef = await ModalService.CreateModalAsync<RenameFlowDialog, RenameFlowModel>
            (
                new ModalOptions { Title = "Rename Flow" }, renameFlowModel
            );

            modalRef.OnOk = async () =>
            {
                try
                {
                    await FlowsViewModel.RenameFlow(id, renameFlowModel.Name);
                    StateHasChanged();
                    
                    // Fire and forget the message to not block modal closing
                    _ = MessageService.SuccessAsync($"Flow renamed to '{renameFlowModel.Name}'");
                }
                catch
                {
                    await MessageService.ErrorAsync($"Flow '{currentName}' could not be renamed");
                }
            };
        }

        private async Task NewFlowDialog()
        {
            var newFlowModel = new NewFlowModel();

            var modalRef = await ModalService.CreateModalAsync<NewFlowDialog, NewFlowModel>
            (
                new ModalOptions { Title = "New Flow" }, newFlowModel
            );

            modalRef.OnOk = async () =>
            {
                try
                {
                    await FlowsViewModel.CreateFlow(newFlowModel.Name);
                    StateHasChanged();
                }
                catch
                {
                    await MessageService.ErrorAsync($"Flow {newFlowModel.Name} could not be created");
                }
            };
        }

        private List<EnhancedEmpty.EmptyActionModel> GetFlowSecondaryActions()
        {
            return new List<EnhancedEmpty.EmptyActionModel>
            {
                new() { Text = "Browse Templates", Icon = "eye", OnClick = () => Task.Run(() => NavigationManager.NavigateTo("/onboarding")) }
            };
        }

        private List<string> GetFlowActionCardBullets()
        {
            return new List<string>
            {
                "Drag-and-drop workflow designer",
                "Connect to APIs and external services",
                "Automated task execution",
                "Real-time monitoring and logging"
            };
        }

        private List<EnhancedEmpty.FeatureCardModel> GetFlowFeatureCards()
        {
            return new List<EnhancedEmpty.FeatureCardModel>
            {
                new() 
                { 
                    Title = "Easily design and manage your Workflows", 
                    Description = "Interact with your Workflows how you want. Our intuitive UI, CLI, API, and Terraform Provider make it simple to manage your automated processes.", 
                    Icon = "setting",
                    IconColor = "#722ed1"
                },
                new() 
                { 
                    Title = "Reliably scale as your demand shifts", 
                    Description = "Resize a Workflow or scale horizontally with many of them. Deploy across globally distributed execution environments with our 99.99% uptime SLA.", 
                    Icon = "thunderbolt",
                    IconColor = "#fa8c16"
                },
                new() 
                { 
                    Title = "Build and monitor, and secure your automations for less", 
                    Description = "Get exceptional performance with pricing starting at $0/month for outbound data transfer - plus monitoring and reporting - for free with every Workflow.", 
                    Icon = "safety-certificate",
                    IconColor = "#52c41a"
                }
            };
        }

        private List<EnhancedEmpty.HelpLinkModel> GetFlowHelpLinks()
        {
            return new List<EnhancedEmpty.HelpLinkModel>
            {
                new() { Text = "Workflows overview", Url = "/onboarding", Icon = "profile", Description = "Detailed information covering all aspects of workflows." },
                new() { Text = "Monitoring your Workflows", Url = "/onboarding", Icon = "bar-chart", Description = "A guide about how to monitor execution performance on Workflows." },
                new() { Text = "Resource Center", Url = "/onboarding", Icon = "book", Description = "Discover how-to resources for managing your Workflows." },
                new() { Text = "Workflows API Docs", Url = "#", Icon = "api", Description = "Use the API to automate workflow creation and updates." }
            };
        }
    }
}
