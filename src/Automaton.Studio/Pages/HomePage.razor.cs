using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using AntDesign;

namespace Automaton.Studio.Pages
{
    public partial class HomePage : ComponentBase
    {
        private bool shouldShowOnboarding = false;
        private bool isLoadingActivity = true;
        private DashboardStats? dashboardStats;
        private IEnumerable<FlowExecution>? recentActivity;
        private IEnumerable<FlowInfo>? recentFlows;

        [Inject] NavigationManager NavigationManager { get; set; }
        [Inject] OnboardingService OnboardingService { get; set; }
        [Inject] DashboardService DashboardService { get; set; }

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

            // Load dashboard data
            await LoadDashboardData();
        }

        private async Task LoadDashboardData()
        {
            try
            {
                // Load stats
                dashboardStats = await DashboardService.GetDashboardStatsAsync();

                // Load recent flows
                recentFlows = await DashboardService.GetRecentFlowsAsync();

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading dashboard data: {ex.Message}");
            }
        }

        private async Task LoadRecentActivity()
        {
            isLoadingActivity = true;
            StateHasChanged();

            try
            {
                recentActivity = await DashboardService.GetRecentActivityAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading recent activity: {ex.Message}");
                recentActivity = new List<FlowExecution>();
            }
            finally
            {
                isLoadingActivity = false;
                StateHasChanged();
            }
        }

        private async Task RefreshActivity()
        {
            await LoadRecentActivity();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                // Load activity data after the initial render to improve perceived performance
                await LoadRecentActivity();
            }
        }

        private void NavigateToCreateFlow()
        {
            NavigationManager.NavigateTo("/flows");
        }

        private void NavigateToCreateStep()
        {
            NavigationManager.NavigateTo("/customsteps");
        }
    }
}
