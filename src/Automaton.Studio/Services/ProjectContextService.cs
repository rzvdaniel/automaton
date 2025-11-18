using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

/// <summary>
/// Service for managing current project context across the application
/// </summary>
public class ProjectContextService
{
    private readonly ProjectsService _projectsService;
    private readonly ILogger _logger = Log.ForContext<ProjectContextService>();
    
    private ProjectListItem? _currentProject;
    private List<ProjectListItem>? _userProjects;
    
    public ProjectContextService(ProjectsService projectsService)
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
            // Always load fresh project details from API to ensure CurrentUserRole is set
            // This is important because the role information needs to be current
            var projectDto = await _projectsService.GetProjectAsync(projectId.Value);
            var project = new ProjectListItem
            {
                Id = projectDto.Id,
                Name = projectDto.Name,
                DisplayName = projectDto.DisplayName,
                Description = projectDto.Description,
                OwnerId = projectDto.OwnerId,
                OwnerName = projectDto.OwnerName,
                CurrentUserRole = projectDto.CurrentUserRole,
                MemberCount = projectDto.MemberCount,
                AppCount = projectDto.AppCount,
                FlowCount = projectDto.FlowCount,
                CreatedAt = projectDto.CreatedAt,
                IsOwner = projectDto.IsOwner
            };
            
            CurrentProject = project;
            _logger.Information("Switched to project {ProjectId} ({ProjectName}) with role {Role}", 
                projectId, project.DisplayName, project.CurrentUserRole);
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
