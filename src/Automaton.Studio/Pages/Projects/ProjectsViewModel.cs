using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Serilog;

namespace Automaton.Studio.Pages.Projects;

public class ProjectsViewModel
{
    private readonly ProjectsClientService _projectsService;
    private readonly ILogger _logger;

    public List<ProjectListItem>? Projects { get; private set; }

    public ProjectsViewModel(ProjectsClientService projectsService, ILogger logger)
    {
        _projectsService = projectsService;
        _logger = logger;
    }

    public async Task LoadProjects()
    {
        try
        {
            _logger.Information("Loading projects list");
            var projectsList = await _projectsService.ListProjectsAsync();
            Projects = projectsList?.ToList();
            _logger.Information("Loaded {Count} projects", Projects?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load projects");
            throw;
        }
    }

    public async Task<ProjectDto> CreateProject(CreateProjectRequest request)
    {
        try
        {
            _logger.Information("Creating project: {Name}", request.Name);
            var project = await _projectsService.CreateProjectAsync(request);
            
            // Reload projects list
            await LoadProjects();
            
            _logger.Information("Project created successfully: {ProjectId}", project.Id);
            return project;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create project: {Name}", request.Name);
            throw;
        }
    }

    public async Task UpdateProject(Guid projectId, UpdateProjectRequest request)
    {
        try
        {
            _logger.Information("Updating project: {ProjectId}", projectId);
            await _projectsService.UpdateProjectAsync(projectId, request);
            
            // Reload projects list
            await LoadProjects();
            
            _logger.Information("Project updated successfully: {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update project: {ProjectId}", projectId);
            throw;
        }
    }

    public async Task DeleteProject(Guid projectId)
    {
        try
        {
            _logger.Information("Deleting project: {ProjectId}", projectId);
            await _projectsService.DeleteProjectAsync(projectId);
            
            // Reload projects list
            await LoadProjects();
            
            _logger.Information("Project deleted successfully: {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete project: {ProjectId}", projectId);
            throw;
        }
    }
}
