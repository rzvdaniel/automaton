using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Serilog;

namespace Automaton.Studio.Pages.Projects;

public class ProjectsViewModel
{
    private readonly ProjectsService _projectsService;

    public List<ProjectListItem>? Projects { get; private set; }

    public ProjectsViewModel(ProjectsService projectsService)
    {
        _projectsService = projectsService;
    }

    public async Task LoadProjects()
    {
        try
        {
            Log.Information("Loading projects list");
            var projectsList = await _projectsService.ListProjectsAsync();
            Projects = projectsList?.ToList();
            Log.Information("Loaded {Count} projects", Projects?.Count ?? 0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load projects");
            throw;
        }
    }

    public async Task<ProjectDto> CreateProject(CreateProjectRequest request)
    {
        try
        {
            Log.Information("Creating project: {Name}", request.Name);
            var project = await _projectsService.CreateProjectAsync(request);
            
            // Reload projects list
            await LoadProjects();

            Log.Information("Project created successfully: {ProjectId}", project.Id);
            return project;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create project: {Name}", request.Name);
            throw;
        }
    }

    public async Task UpdateProject(Guid projectId, UpdateProjectRequest request)
    {
        try
        {
            Log.Information("Updating project: {ProjectId}", projectId);
            await _projectsService.UpdateProjectAsync(projectId, request);
            
            // Reload projects list
            await LoadProjects();

            Log.Information("Project updated successfully: {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update project: {ProjectId}", projectId);
            throw;
        }
    }

    public async Task DeleteProject(Guid projectId)
    {
        try
        {
            Log.Information("Deleting project: {ProjectId}", projectId);
            await _projectsService.DeleteProjectAsync(projectId);
            
            // Reload projects list
            await LoadProjects();

            Log.Information("Project deleted successfully: {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete project: {ProjectId}", projectId);
            throw;
        }
    }
}
