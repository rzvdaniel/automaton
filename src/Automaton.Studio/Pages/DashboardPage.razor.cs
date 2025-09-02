using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages
{
    public partial class DashboardPage : ComponentBase
    {
        private bool shouldShowOnboarding;
        private bool isLoadingActivity = true;
        private bool isLoadingExecutions = true;
        private DashboardStats? dashboardStats;
        private IEnumerable<RecentActivity>? recentActivity;
        private IEnumerable<FlowExecution>? recentExecutions;
        private IEnumerable<FlowInfo>? recentFlows;
        private int activityCount = 10;
        private int executionCount = 15;

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

                // Load more recent flows for the dedicated tab
                recentFlows = await DashboardService.GetAllRecentFlowsAsync();

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
                recentActivity = await DashboardService.GetRecentActivityAsync(activityCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading recent activity: {ex.Message}");
                recentActivity = new List<RecentActivity>();
            }
            finally
            {
                isLoadingActivity = false;
                StateHasChanged();
            }
        }

        private async Task LoadRecentExecutions()
        {
            isLoadingExecutions = true;
            StateHasChanged();

            try
            {
                recentExecutions = await DashboardService.GetRecentExecutionsAsync(executionCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading recent executions: {ex.Message}");
                recentExecutions = new List<FlowExecution>();
            }
            finally
            {
                isLoadingExecutions = false;
                StateHasChanged();
            }
        }

        private async Task RefreshActivity()
        {
            activityCount = 10; // Reset to initial count
            await LoadRecentActivity();
        }

        private async Task RefreshExecutions()
        {
            executionCount = 15; // Reset to initial count
            await LoadRecentExecutions();
        }

        private async Task LoadMoreActivity()
        {
            activityCount += 10;
            await LoadRecentActivity();
        }

        private async Task LoadMoreExecutions()
        {
            executionCount += 15;
            await LoadRecentExecutions();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                // Load activity data after the initial render to improve perceived performance
                await LoadRecentActivity();
                await LoadRecentExecutions();
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

        private string GetActivityTagColor(ActivityType activityType)
        {
            return activityType switch
            {
                ActivityType.FlowCreated or ActivityType.CustomStepCreated or ActivityType.RunnerAdded or ActivityType.ScheduleCreated => "green",
                ActivityType.FlowUpdated or ActivityType.CustomStepUpdated or ActivityType.RunnerUpdated or ActivityType.ScheduleUpdated => "blue",
                ActivityType.FlowDeleted or ActivityType.CustomStepDeleted or ActivityType.RunnerDeleted or ActivityType.ScheduleDeleted => "red",
                ActivityType.FlowExecuted => "purple",
                _ => "default"
            };
        }

        private string GetExecutionStatusColor(WorkflowStatus status)
        {
            return status switch
            {
                WorkflowStatus.Success => "green",
                WorkflowStatus.Error => "red",
                WorkflowStatus.Working => "blue",
                _ => "default"
            };
        }
    }
}
