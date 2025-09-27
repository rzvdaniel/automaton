using AntDesign;
using Automaton.Studio.Models;
using Automaton.Studio.Pages.CustomSteps.Components.NewCustomStepDialog;
using Automaton.Studio.Shared.Components;
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;

namespace Automaton.Studio.Pages.CustomSteps
{
    partial class CustomStepsPage : ComponentBase
    {
        public bool loading;

        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private CustomStepsViewModel CustomStepsViewModel { get; set; } = default!;
        [Inject] private ModalService ModalService { get; set; }
        [Inject] private MessageService MessageService { get; set; }

        protected override async Task OnInitializedAsync()
        {
            loading = true;

            try
            {
                await CustomStepsViewModel.GetCustomSteps();
            }
            catch
            {
                await MessageService.ErrorAsync(Resources.Errors.CustomStepsListNotLoaded);
            }
            finally
            {
                loading = false;
            }

            await base.OnInitializedAsync();
        }

        private void EditCustomStep(Guid id)
        {
            NavigationManager.NavigateTo($"stepdesigner/{id}");
        }

        private async Task DeleteCustomStep(Guid id)
        {
            await CustomStepsViewModel.DeleteCustomStep(id);

            StateHasChanged();
        }

        private async Task ShowNewCustomStepDialog()
        {
            var model = new NewCustomStep();

            var modalRef = await ModalService.CreateModalAsync<NewCustomStepDialog, NewCustomStep>
            (
                new ModalOptions { Title = "New Custom Step" }, model
            );

            modalRef.OnOk = async () =>
            {
                try
                {
                    await CustomStepsViewModel.CreateCustomStep(model);
                    StateHasChanged();
                }
                catch
                {
                    await MessageService.ErrorAsync($"Custom step {model.Name} could not be created");
                }
            };
        }

        private List<EnhancedEmpty.EmptyActionModel> GetCustomStepSecondaryActions()
        {
            return new List<EnhancedEmpty.EmptyActionModel>
            {
                new() { Text = "Browse Templates", Icon = "eye", OnClick = () => Task.FromResult(0) },
                new() { Text = "View Documentation", Icon = "book", OnClick = () => Task.FromResult(0) }
            };
        }

        private List<EnhancedEmpty.FeatureCardModel> GetCustomStepFeatureCards()
        {
            return new List<EnhancedEmpty.FeatureCardModel>
            {
                new() 
                { 
                    Title = "Python Scripts", 
                    Description = "Write powerful automation scripts in Python", 
                    Icon = "code",
                    IconColor = "#52c41a"
                },
                new() 
                { 
                    Title = "PowerShell Commands", 
                    Description = "Execute Windows PowerShell commands and scripts", 
                    Icon = "console-sql",
                    IconColor = "#1890ff"
                },
                new() 
                { 
                    Title = "Batch Processing", 
                    Description = "Run batch files and command-line utilities", 
                    Icon = "file-text",
                    IconColor = "#722ed1"
                }
            };
        }

        private List<EnhancedEmpty.HelpLinkModel> GetCustomStepHelpLinks()
        {
            return new List<EnhancedEmpty.HelpLinkModel>
            {
                new() { Text = "Scripting Guide", Url = "#", Icon = "code" },
                new() { Text = "Step Templates", Url = "#", Icon = "file-copy" },
                new() { Text = "API Reference", Url = "#", Icon = "api" }
            };
        }
    }
}
