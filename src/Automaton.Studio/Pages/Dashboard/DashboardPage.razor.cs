using Automaton.Studio.Enums;
using Automaton.Studio.Models;
using Automaton.Studio.Pages.Dashboard.Components;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Dashboard
{
    public partial class DashboardPage : ComponentBase, IDisposable
    {
        private bool shouldShowOnboarding;
        private ProjectRole? currentUserRole;
        private bool isInProjectContext;

        [Inject] private NavigationManager NavigationManager { get; set; }
        [Inject] private OnboardingService OnboardingService { get; set; }
        [Inject] private ProjectContextService ProjectContextService { get; set; }
        [Inject] private DashboardContextService DashboardContextService { get; set; }
        [Inject] private DashboardViewModel DashboardViewModel { get; set; } = default!;

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

            // Subscribe to project context changes
            ProjectContextService.OnCurrentProjectChanged += HandleProjectContextChanged;

            // Detect user role in current project
            await DetectUserRoleAsync();

            // Load dashboard context for role-based data
            await DashboardContextService.LoadDashboardDataAsync();         
        }

        private async void HandleProjectContextChanged(ProjectListItem? project)
        {
            Console.WriteLine($"Project context changed: {project?.DisplayName ?? "Personal Workspace"}");
            
            // Re-detect user role
            await DetectUserRoleAsync();
            
            // Reload dashboard context for new role
            await DashboardContextService.LoadDashboardDataAsync();
                        
            // Trigger UI update
            StateHasChanged();
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

        /// <summary>
        /// Maps user role and project context to the appropriate dashboard component type.
        /// Always returns a valid Type (defaults to PersonalDashboard if unable to determine).
        /// </summary>
        /// <returns>The dashboard component type to display.</returns>
        protected Type GetDashboardComponentType()
        {
            // If not in project context, show personal workspace dashboard
            if (!isInProjectContext)
            {
                return typeof(PersonalDashboard);
            }

            // If role is not available, default to personal dashboard
            if (!currentUserRole.HasValue)
            {
                return typeof(PersonalDashboard);
            }

            // Map role to dashboard component type
            return currentUserRole.Value switch
            {
                ProjectRole.AppUser => typeof(AppUserDashboard),
                ProjectRole.Viewer => typeof(ViewerDashboard),
                ProjectRole.Developer => typeof(DeveloperDashboard),
                ProjectRole.ProjectAdmin => typeof(DeveloperDashboard),
                _ => typeof(PersonalDashboard) // Fallback for unknown roles
            };
        }

        public void Dispose()
        {
            // Unsubscribe from event to prevent memory leaks
            ProjectContextService.OnCurrentProjectChanged -= HandleProjectContextChanged;
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
    }
}
