using AntDesign;
using Automaton.Core.Enums;
using Automaton.Studio.Pages.Flows.Components.NewFlow;
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
                await FlowsViewModel.GetRunners();
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

        private string GetRunnerStatusIcon(RunnerStatus status)
        {
            return status switch
            {
                RunnerStatus.Online => "check-circle",
                RunnerStatus.Offline => "close-circle",
                _ => "question-circle"
            };
        }

        private string GetRunnerStatusClass(RunnerStatus status)
        {
            return $"runner-status-icon runner-status-{status.ToString().ToLower()}";
        }

        private List<EnhancedEmpty.EmptyActionModel> GetSecondaryActions()
        {
            return new List<EnhancedEmpty.EmptyActionModel>
            {
                new() { Text = "Browse Examples", Icon = "eye", OnClick = () => Task.Run(() => NavigationManager.NavigateTo("/onboarding")) },
                new() { Text = "View Documentation", Icon = "book", OnClick = () => Task.FromResult(0) }
            };
        }

        private List<EnhancedEmpty.FeatureCardModel> GetFlowFeatureCards()
        {
            return new List<EnhancedEmpty.FeatureCardModel>
            {
                new() 
                { 
                    Title = "Automate File Tasks", 
                    Description = "Move, copy, and process files automatically", 
                    Icon = "folder",
                    IconColor = "#52c41a",
                    OnClick = () => Task.Run(() => NavigationManager.NavigateTo("/onboarding"))
                },
                new() 
                { 
                    Title = "API Integration", 
                    Description = "Connect to REST APIs and web services", 
                    Icon = "api",
                    IconColor = "#722ed1",
                    OnClick = () => Task.Run(() => NavigationManager.NavigateTo("/onboarding"))
                },
                new() 
                { 
                    Title = "Data Processing", 
                    Description = "Transform and analyze data automatically", 
                    Icon = "bar-chart",
                    IconColor = "#1890ff",
                    OnClick = () => Task.Run(() => NavigationManager.NavigateTo("/onboarding"))
                }
            };
        }

        private List<EnhancedEmpty.HelpLinkModel> GetFlowHelpLinks()
        {
            return new List<EnhancedEmpty.HelpLinkModel>
            {
                new() { Text = "Getting Started Guide", Url = "/onboarding", Icon = "play-circle" },
                new() { Text = "Workflow Examples", Url = "/onboarding", Icon = "experiment" },
                new() { Text = "Documentation", Url = "#", Icon = "book" }
            };
        }
    }
}
