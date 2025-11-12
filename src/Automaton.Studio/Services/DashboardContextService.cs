using Automaton.Studio.Enums;
using Automaton.Studio.Models;
using Serilog;

namespace Automaton.Studio.Services;

/// <summary>
/// Service for managing dashboard context and determining user's primary role
/// </summary>
public class DashboardContextService
{
    private readonly ProjectContextService _projectContext;
    private readonly ProjectResourcesClientService _projectResources;
    private readonly ILogger _logger;

    public bool IsAppUser { get; private set; }
    public bool IsDeveloper { get; private set; }
    public bool IsProjectAdmin { get; private set; }
    public List<ProjectAppDto> ProjectApps { get; private set; } = new();
    public bool IsLoading { get; private set; }

    public event Action? OnDashboardDataChanged;

    public DashboardContextService(
        ProjectContextService projectContext,
        ProjectResourcesClientService projectResources)
    {
        _projectContext = projectContext;
        _projectResources = projectResources;
        _logger = Log.ForContext<DashboardContextService>();

        // Subscribe to project context changes
        _projectContext.OnCurrentProjectChanged += HandleProjectChanged;
    }

    private async void HandleProjectChanged(ProjectListItem? project)
    {
        await LoadDashboardDataAsync();
    }

    public async Task LoadDashboardDataAsync()
    {
        IsLoading = true;
        OnDashboardDataChanged?.Invoke();

        try
        {
            // Determine user's primary role based on project context
            if (_projectContext.IsInProjectContext() && _projectContext.CurrentProject != null)
            {
                var currentRole = _projectContext.CurrentProject.CurrentUserRole;

                IsAppUser = currentRole == ProjectRole.AppUser;
                IsDeveloper = currentRole == ProjectRole.Developer;
                IsProjectAdmin = currentRole == ProjectRole.ProjectAdmin;

                // Load project apps if user is AppUser
                if (IsAppUser)
                {
                    var projectId = _projectContext.GetCurrentProjectId()!.Value;
                    ProjectApps = await _projectResources.ListAppsAsync(projectId);
                    _logger.Information("Loaded {Count} project apps for AppUser", ProjectApps.Count);
                }
                else
                {
                    ProjectApps = new List<ProjectAppDto>();
                }
            }
            else
            {
                // Personal workspace - user is a creator/developer
                IsAppUser = false;
                IsDeveloper = true;
                IsProjectAdmin = false;
                ProjectApps = new List<ProjectAppDto>();
            }

            _logger.Information("Dashboard context loaded: AppUser={IsAppUser}, Developer={IsDeveloper}, ProjectAdmin={IsProjectAdmin}",
                IsAppUser, IsDeveloper, IsProjectAdmin);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load dashboard data");
        }
        finally
        {
            IsLoading = false;
            OnDashboardDataChanged?.Invoke();
        }
    }

    public async Task RefreshAsync()
    {
        await LoadDashboardDataAsync();
    }
}
