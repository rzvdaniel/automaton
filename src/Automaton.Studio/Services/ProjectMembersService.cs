using Automaton.Studio.Models;
using Serilog;

namespace Automaton.Studio.Services;

public class ProjectMembersService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly ILogger logger;

    public ProjectMembersService(
        ConfigurationService configService,
        HttpClient httpClient
    )
    {
        logger = Log.ForContext<ProjectMembersService>();
        this.configService = configService;
        this.httpClient = httpClient;
    }

    public async Task<ICollection<ProjectMemberDto>> ListMembersAsync(Guid projectId)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.ProjectsUrl}/{projectId}/members");
            result.EnsureSuccessStatusCode();
            var members = await result.Content.ReadAsAsync<ICollection<ProjectMemberDto>>();
            return members;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when loading members for project {ProjectId}.", projectId);
            throw;
        }
    }

    public async Task<ProjectMemberDto> GetMemberAsync(Guid projectId, string userId)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.ProjectsUrl}/{projectId}/members/{userId}");
            result.EnsureSuccessStatusCode();
            var member = await result.Content.ReadAsAsync<ProjectMemberDto>();
            return member;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when loading member {UserId} for project {ProjectId}.", userId, projectId);
            throw;
        }
    }

    public async Task<ProjectMemberDto> AddMemberAsync(Guid projectId, AddProjectMemberRequest request)
    {
        try
        {
            var result = await httpClient.PostAsJsonAsync($"{configService.ProjectsUrl}/{projectId}/members", request);
            result.EnsureSuccessStatusCode();
            var member = await result.Content.ReadAsAsync<ProjectMemberDto>();
            logger.Information("Added member {Email} to project {ProjectId} with role {Role}.", request.Email, projectId, request.Role);
            return member;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when adding member to project {ProjectId}.", projectId);
            throw;
        }
    }

    public async Task UpdateMemberRoleAsync(Guid projectId, string userId, UpdateProjectMemberRequest request)
    {
        try
        {
            var result = await httpClient.PutAsJsonAsync($"{configService.ProjectsUrl}/{projectId}/members/{userId}", request);
            result.EnsureSuccessStatusCode();
            logger.Information("Updated member {UserId} role to {Role} in project {ProjectId}.", userId, request.Role, projectId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when updating member {UserId} in project {ProjectId}.", userId, projectId);
            throw;
        }
    }

    public async Task RemoveMemberAsync(Guid projectId, string userId)
    {
        try
        {
            var result = await httpClient.DeleteAsync($"{configService.ProjectsUrl}/{projectId}/members/{userId}");
            result.EnsureSuccessStatusCode();
            logger.Information("Removed member {UserId} from project {ProjectId}.", userId, projectId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when removing member {UserId} from project {ProjectId}.", userId, projectId);
            throw;
        }
    }
}
