using Automaton.Studio.Models;
using Serilog;

namespace Automaton.Studio.Services;

public class ProjectResourcesClientService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly ILogger logger;

    public ProjectResourcesClientService(
        ConfigurationService configService,
        HttpClient httpClient
    )
    {
        logger = Log.ForContext<ProjectResourcesClientService>();
        this.configService = configService;
        this.httpClient = httpClient;
    }

    // Apps
    public async Task<ICollection<ProjectAppDto>> ListAppsAsync(Guid projectId)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.ProjectsUrl}/{projectId}/apps");
            result.EnsureSuccessStatusCode();
            var apps = await result.Content.ReadAsAsync<ICollection<ProjectAppDto>>();
            return apps;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when loading apps for project {ProjectId}.", projectId);
            throw;
        }
    }

    public async Task<ProjectAppDto> AddAppAsync(Guid projectId, Guid appId)
    {
        try
        {
            var result = await httpClient.PostAsync($"{configService.ProjectsUrl}/{projectId}/apps/{appId}", null);
            result.EnsureSuccessStatusCode();
            var projectApp = await result.Content.ReadAsAsync<ProjectAppDto>();
            logger.Information("Added app {AppId} to project {ProjectId}.", appId, projectId);
            return projectApp;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when adding app {AppId} to project {ProjectId}.", appId, projectId);
            throw;
        }
    }

    public async Task RemoveAppAsync(Guid projectId, Guid appId)
    {
        try
        {
            var result = await httpClient.DeleteAsync($"{configService.ProjectsUrl}/{projectId}/apps/{appId}");
            result.EnsureSuccessStatusCode();
            logger.Information("Removed app {AppId} from project {ProjectId}.", appId, projectId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when removing app {AppId} from project {ProjectId}.", appId, projectId);
            throw;
        }
    }

    // Flows
    public async Task<ICollection<ProjectFlowDto>> ListFlowsAsync(Guid projectId)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.ProjectsUrl}/{projectId}/flows");
            result.EnsureSuccessStatusCode();
            var flows = await result.Content.ReadAsAsync<ICollection<ProjectFlowDto>>();
            return flows;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when loading flows for project {ProjectId}.", projectId);
            throw;
        }
    }

    public async Task<ProjectFlowDto> AddFlowAsync(Guid projectId, Guid flowId)
    {
        try
        {
            var result = await httpClient.PostAsync($"{configService.ProjectsUrl}/{projectId}/flows/{flowId}", null);
            result.EnsureSuccessStatusCode();
            var projectFlow = await result.Content.ReadAsAsync<ProjectFlowDto>();
            logger.Information("Added flow {FlowId} to project {ProjectId}.", flowId, projectId);
            return projectFlow;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when adding flow {FlowId} to project {ProjectId}.", flowId, projectId);
            throw;
        }
    }

    public async Task RemoveFlowAsync(Guid projectId, Guid flowId)
    {
        try
        {
            var result = await httpClient.DeleteAsync($"{configService.ProjectsUrl}/{projectId}/flows/{flowId}");
            result.EnsureSuccessStatusCode();
            logger.Information("Removed flow {FlowId} from project {ProjectId}.", flowId, projectId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when removing flow {FlowId} from project {ProjectId}.", flowId, projectId);
            throw;
        }
    }

    // Runners
    public async Task<ICollection<ProjectRunnerDto>> ListRunnersAsync(Guid projectId)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.ProjectsUrl}/{projectId}/runners");
            result.EnsureSuccessStatusCode();
            var runners = await result.Content.ReadAsAsync<ICollection<ProjectRunnerDto>>();
            return runners;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when loading runners for project {ProjectId}.", projectId);
            throw;
        }
    }

    public async Task<ProjectRunnerDto> AddRunnerAsync(Guid projectId, Guid runnerId)
    {
        try
        {
            var result = await httpClient.PostAsync($"{configService.ProjectsUrl}/{projectId}/runners/{runnerId}", null);
            result.EnsureSuccessStatusCode();
            var projectRunner = await result.Content.ReadAsAsync<ProjectRunnerDto>();
            logger.Information("Added runner {RunnerId} to project {ProjectId}.", runnerId, projectId);
            return projectRunner;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when adding runner {RunnerId} to project {ProjectId}.", runnerId, projectId);
            throw;
        }
    }

    public async Task RemoveRunnerAsync(Guid projectId, Guid runnerId)
    {
        try
        {
            var result = await httpClient.DeleteAsync($"{configService.ProjectsUrl}/{projectId}/runners/{runnerId}");
            result.EnsureSuccessStatusCode();
            logger.Information("Removed runner {RunnerId} from project {ProjectId}.", runnerId, projectId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when removing runner {RunnerId} from project {ProjectId}.", runnerId, projectId);
            throw;
        }
    }

    // Custom Steps
    public async Task<ICollection<ProjectCustomStepDto>> ListCustomStepsAsync(Guid projectId)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.ProjectsUrl}/{projectId}/customsteps");
            result.EnsureSuccessStatusCode();
            var steps = await result.Content.ReadAsAsync<ICollection<ProjectCustomStepDto>>();
            return steps;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when loading custom steps for project {ProjectId}.", projectId);
            throw;
        }
    }

    public async Task<ProjectCustomStepDto> AddCustomStepAsync(Guid projectId, Guid stepId)
    {
        try
        {
            var result = await httpClient.PostAsync($"{configService.ProjectsUrl}/{projectId}/customsteps/{stepId}", null);
            result.EnsureSuccessStatusCode();
            var projectStep = await result.Content.ReadAsAsync<ProjectCustomStepDto>();
            logger.Information("Added custom step {StepId} to project {ProjectId}.", stepId, projectId);
            return projectStep;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when adding custom step {StepId} to project {ProjectId}.", stepId, projectId);
            throw;
        }
    }

    public async Task RemoveCustomStepAsync(Guid projectId, Guid stepId)
    {
        try
        {
            var result = await httpClient.DeleteAsync($"{configService.ProjectsUrl}/{projectId}/customsteps/{stepId}");
            result.EnsureSuccessStatusCode();
            logger.Information("Removed custom step {StepId} from project {ProjectId}.", stepId, projectId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error happened when removing custom step {StepId} from project {ProjectId}.", stepId, projectId);
            throw;
        }
    }
}
