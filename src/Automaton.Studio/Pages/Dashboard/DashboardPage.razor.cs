using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Automaton.Core.Enums;
using Automaton.Studio.Enums;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Dashboard
{
    public partial class DashboardPage : ComponentBase
    {
        private bool shouldShowOnboarding;
        private ProjectRole? currentUserRole;
        private bool isInProjectContext;

        [Inject] NavigationManager NavigationManager { get; set; }
        [Inject] OnboardingService OnboardingService { get; set; }
        [Inject] ProjectContextService ProjectContextService { get; set; }
        [Inject] DashboardContextService DashboardContextService { get; set; }

        protected override async Task OnInitializedAsync()
        {
            try
            {
                shouldShowOnboarding = await OnboardingService.TryShouldShowOnboardingAsync();
            }
            catch
            {
                shouldShowOnboarding = false;
            }

            // Detect user role in current project
            await DetectUserRoleAsync();

            // Load dashboard context for role-based data
            await DashboardContextService.LoadDashboardDataAsync();
        }

        private async Task DetectUserRoleAsync()
        {
            try
            {
                var currentProject = ProjectContextService.CurrentProject;
                isInProjectContext = currentProject != null;

                if (isInProjectContext)
                {
                    currentUserRole = currentProject!.CurrentUserRole;
                    Console.WriteLine($"User role in project: {currentUserRole}");
                }
                else
                {
                    // Personal workspace - user is a creator/developer
                    currentUserRole = null;
                    Console.WriteLine("User in personal workspace");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error detecting user role: {ex.Message}");
                currentUserRole = null;
                isInProjectContext = false;
            }
        }

        private void NavigateToCreateApp()
        {
            NavigationManager.NavigateTo("/apps");
        }

        private void NavigateToCreateFlow()
        {
            NavigationManager.NavigateTo("/flows");
        }

        private void NavigateToCreateStep()
        {
            NavigationManager.NavigateTo("/customsteps");
        }

        // Removed the skip functionality since we're using a simplified approach
        // Users can simply not visit the onboarding page if they don't want to complete setup
        private void SkipOnboardingForNow()
        {
            // Update the local state to hide the onboarding alert for this session only
            shouldShowOnboarding = false;
            StateHasChanged();
        }
    }
}
