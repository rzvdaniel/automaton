using Automaton.Studio.Models;
using Serilog;

namespace Automaton.Studio.Services;

/// <summary>
/// Service for managing current project context across the application
/// </summary>
public class ProjectContextService
{
    private readonly ProjectsClientService _projectsService;
    private readonly ILogger _logger = Log.ForContext<ProjectContextService>();
    
    private ProjectListItem? _currentProject;
    private List<ProjectListItem>? _userProjects;
    
    public ProjectContextService(ProjectsClientService projectsService)
    {
        _projectsService = projectsService;
    }
    
    /// <summary>
    /// Current active project (null if none selected or in personal mode)
    /// </summary>
    public ProjectListItem? CurrentProject
    {
        get => _currentProject;
        private set
        {
            _currentProject = value;
            OnCurrentProjectChanged?.Invoke(value);
        }
    }
    
    /// <summary>
    /// Event raised when current project changes
    /// </summary>
    public event Action<ProjectListItem?>? OnCurrentProjectChanged;
    
    /// <summary>
    /// Load all user's projects
    /// </summary>
    public async Task LoadProjectsAsync()
    {
        try
        {
            var projects = await _projectsService.ListProjectsAsync();
            _userProjects = projects?.ToList();
            _logger.Information("Loaded {Count} projects for user", _userProjects?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load user projects");
            _userProjects = new List<ProjectListItem>();
        }
    }
    
    /// <summary>
    /// Get all user's projects
    /// </summary>
    public List<ProjectListItem> GetProjects()
    {
        return _userProjects ?? new List<ProjectListItem>();
    }
    
    /// <summary>
    /// Set current project context
    /// </summary>
    public async Task SetCurrentProjectAsync(Guid? projectId)
    {
        if (projectId == null)
        {
            CurrentProject = null;
            _logger.Information("Switched to personal workspace");
            return;
        }
        
        try
        {
            // Find project in loaded list first
            var project = _userProjects?.FirstOrDefault(p => p.Id == projectId);
            
            // If not found in cache, try to load from API
            if (project == null)
            {
                var projectDto = await _projectsService.GetProjectAsync(projectId.Value);
                project = new ProjectListItem
                {
                    Id = projectDto.Id,
                    Name = projectDto.Name,
                    DisplayName = projectDto.DisplayName,
                    Description = projectDto.Description,
                    OwnerName = projectDto.OwnerName,
                    CurrentUserRole = projectDto.CurrentUserRole,
                    MemberCount = projectDto.MemberCount,
                    CreatedAt = projectDto.CreatedAt
                };
            }
            
            CurrentProject = project;
            _logger.Information("Switched to project {ProjectId} ({ProjectName})", projectId, project.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set current project {ProjectId}", projectId);
            CurrentProject = null;
        }
    }
    
    /// <summary>
    /// Clear current project context (switch to personal workspace)
    /// </summary>
    public void ClearCurrentProject()
    {
        CurrentProject = null;
    }
    
    /// <summary>
    /// Check if user is in a project context
    /// </summary>
    public bool IsInProjectContext()
    {
        return CurrentProject != null;
    }
    
    /// <summary>
    /// Get current project ID (null if in personal workspace)
    /// </summary>
    public Guid? GetCurrentProjectId()
    {
        return CurrentProject?.Id;
    }
}
