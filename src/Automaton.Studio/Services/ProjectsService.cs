using Automaton.Studio.Models;
using Serilog;

namespace Automaton.Studio.Services;

public class ProjectsService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly ILogger logger;

    public ProjectsService(
        ConfigurationService configService,
        HttpClient httpClient
    )
    {
        logger = Log.ForContext<ProjectsService>();
        this.configService = configService;
        this.httpClient = httpClient;
    }

    public async Task<ICollection<ProjectListItem>> ListProjectsAsync()
    {
        try
        {
            var result = await httpClient.GetAsync(configService.ProjectsUrl);
            result.EnsureSuccessStatusCode();
            var projects = await result.Content.ReadAsAsync<ICollection<ProjectListItem>>();
            return projects;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when loading projects list.");
            throw;
        }
    }

    public async Task<ProjectDto> GetProjectAsync(Guid projectId)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.ProjectsUrl}/{projectId}");
            result.EnsureSuccessStatusCode();
            var project = await result.Content.ReadAsAsync<ProjectDto>();
            return project;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when loading project {ProjectId}.", projectId);
            throw;
        }
    }

    public async Task<ProjectDto> CreateProjectAsync(CreateProjectRequest request)
    {
        try
        {
            var result = await httpClient.PostAsJsonAsync(configService.ProjectsUrl, request);
            result.EnsureSuccessStatusCode();
            var project = await result.Content.ReadAsAsync<ProjectDto>();
            logger.Information("Created project {ProjectId} with name {ProjectName}.", project.Id, project.Name);
            return project;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when creating project.");
            throw;
        }
    }

    public async Task UpdateProjectAsync(Guid projectId, UpdateProjectRequest request)
    {
        try
        {
            var result = await httpClient.PutAsJsonAsync($"{configService.ProjectsUrl}/{projectId}", request);
            result.EnsureSuccessStatusCode();
            logger.Information("Updated project {ProjectId}.", projectId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when updating project {ProjectId}.", projectId);
            throw;
        }
    }

    public async Task DeleteProjectAsync(Guid projectId)
    {
        try
        {
            var result = await httpClient.DeleteAsync($"{configService.ProjectsUrl}/{projectId}");
            result.EnsureSuccessStatusCode();
            logger.Information("Deleted project {ProjectId}.", projectId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when deleting project {ProjectId}.", projectId);
            throw;
        }
    }

    public async Task<ProjectMembershipDto> GetMembershipAsync(Guid projectId)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.ProjectsUrl}/{projectId}/membership");
            result.EnsureSuccessStatusCode();
            var membership = await result.Content.ReadAsAsync<ProjectMembershipDto>();
            return membership;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when checking membership for project {ProjectId}.", projectId);
            throw;
        }
    }
}
