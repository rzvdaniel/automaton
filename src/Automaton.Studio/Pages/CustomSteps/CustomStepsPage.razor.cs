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
                new() { Text = "Browse Templates", Icon = "eye", OnClick = () => Task.FromResult(0) }
            };
        }

        private List<string> GetCustomStepActionCardBullets()
        {
            return new List<string>
            {
                "Support for Python, PowerShell, and Batch",
                "Built-in code editor with syntax highlighting",
                "Reusable across multiple workflows",
                "Version control and testing capabilities"
            };
        }

        private List<EnhancedEmpty.FeatureCardModel> GetCustomStepFeatureCards()
        {
            return new List<EnhancedEmpty.FeatureCardModel>
            {
                new() 
                { 
                    Title = "Easily create and manage your Custom Steps", 
                    Description = "Write powerful automation scripts in Python, PowerShell, or Batch. Our integrated editor makes it simple to develop and test your custom components.", 
                    Icon = "code",
                    IconColor = "#722ed1"
                },
                new() 
                { 
                    Title = "Reusable components across workflows", 
                    Description = "Create once, use everywhere. Your custom steps can be used in multiple workflows, promoting consistency and reducing development time.", 
                    Icon = "retweet",
                    IconColor = "#fa8c16"
                },
                new() 
                { 
                    Title = "Version control and secure execution", 
                    Description = "Track changes with built-in versioning and execute your scripts securely in isolated environments with full logging and monitoring.", 
                    Icon = "safety-certificate",
                    IconColor = "#52c41a"
                }
            };
        }

        private List<EnhancedEmpty.HelpLinkModel> GetCustomStepHelpLinks()
        {
            return new List<EnhancedEmpty.HelpLinkModel>
            {
                new() { Text = "Custom Steps overview", Url = "#", Icon = "experiment", Description = "Detailed information covering all aspects of custom steps." },
                new() { Text = "Scripting your Steps", Url = "#", Icon = "code", Description = "A guide about how to write effective automation scripts." },
                new() { Text = "Step Templates", Url = "#", Icon = "file-copy", Description = "Discover pre-built templates for common automation tasks." },
                new() { Text = "Steps API Reference", Url = "#", Icon = "api", Description = "Use the API to programmatically manage your custom steps." }
            };
        }
    }
}
