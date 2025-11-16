using Automaton.Studio.Enums;
using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

/// <summary>
/// Service for managing role-based dashboard personalization.
/// Determines user's primary role and loads appropriate dashboard data.
/// </summary>
public class ProjectDashboardService
{
    private readonly ProjectContextService _projectContext;
    private readonly ProjectResourcesService _projectResources;
    private readonly ProjectsService _projectsClient;
    private readonly ILogger _logger;

    public ProjectDashboardService(
        ProjectContextService projectContext,
        ProjectResourcesService projectResources,
        ProjectsService projectsClient)
    {
        _projectContext = projectContext;
        _projectResources = projectResources;
        _projectsClient = projectsClient;
        _logger = Log.ForContext<ProjectDashboardService>();
    }

    /// <summary>
    /// Indicates if the user is an AppUser (sees app portal)
    /// </summary>
    public bool IsAppUser { get; private set; }

    /// <summary>
    /// Indicates if the user is a Developer (sees creator dashboard)
    /// </summary>
    public bool IsDeveloper { get; private set; }

    /// <summary>
    /// Indicates if the user is a ProjectAdmin (sees creator dashboard with full access)
    /// </summary>
    public bool IsProjectAdmin { get; private set; }

    /// <summary>
    /// Project apps available to the user (for AppUser portal)
    /// </summary>
    public List<ProjectAppDto> ProjectApps { get; private set; } = new();

    /// <summary>
    /// Stats for creator dashboard
    /// </summary>
    public DashboardStatistics Stats { get; private set; } = new();

    /// <summary>
    /// Load dashboard data based on current context and user role
    /// </summary>
    public async Task LoadDashboardDataAsync()
    {
        try
        {
            _logger.Information("Loading dashboard data");

            // Check if user is in a project context
            if (_projectContext.IsInProjectContext() && _projectContext.CurrentProject != null)
            {
                var role = _projectContext.CurrentProject.CurrentUserRole;
                
                _logger.Information("User in project context with role: {Role}", role);

                // Determine user's role
                IsAppUser = role == ProjectRole.AppUser;
                IsDeveloper = role == ProjectRole.Developer;
                IsProjectAdmin = role == ProjectRole.ProjectAdmin;

                if (IsAppUser)
                {
                    // Load project apps for AppUser portal
                    await LoadProjectAppsAsync();
                }
                else
                {
                    // Load stats for creator dashboard
                    await LoadDashboardStatsAsync();
                }
            }
            else
            {
                // Personal workspace - default to creator dashboard
                _logger.Information("User in personal workspace");
                IsAppUser = false;
                IsDeveloper = false;
                IsProjectAdmin = false;
                await LoadDashboardStatsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load dashboard data");
        }
    }

    /// <summary>
    /// Load project apps for AppUser portal
    /// </summary>
    private async Task LoadProjectAppsAsync()
    {
        try
        {
            if (_projectContext.GetCurrentProjectId() is Guid projectId)
            {
                ProjectApps = [.. await _projectResources.ListAppsAsync(projectId)];
                _logger.Information("Loaded {Count} project apps for AppUser", ProjectApps.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load project apps");
            ProjectApps = new List<ProjectAppDto>();
        }
    }

    /// <summary>
    /// Load stats for creator dashboard
    /// </summary>
    private async Task LoadDashboardStatsAsync()
    {
        try
        {
            var projects = await _projectsClient.ListProjectsAsync();
            
            // Calculate stats
            Stats = new DashboardStatistics
            {
                TotalProjects = projects.Count,
                TotalApps = projects.Sum(p => p.AppCount),
                TotalFlows = projects.Sum(p => p.FlowCount),
                TotalMembers = projects.Sum(p => p.MemberCount)
            };

            _logger.Information("Loaded dashboard stats: {Projects} projects, {Apps} apps", 
                Stats.TotalProjects, Stats.TotalApps);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load dashboard stats");
            Stats = new DashboardStatistics();
        }
    }

    /// <summary>
    /// Refresh dashboard data
    /// </summary>
    public async Task RefreshAsync()
    {
        await LoadDashboardDataAsync();
    }
}

/// <summary>
/// Dashboard statistics for creator view
/// </summary>
public class DashboardStatistics
{
    public int TotalProjects { get; set; }
    public int TotalApps { get; set; }
    public int TotalFlows { get; set; }
    public int TotalMembers { get; set; }
}
