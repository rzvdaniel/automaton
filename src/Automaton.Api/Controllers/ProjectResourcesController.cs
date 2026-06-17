using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// API controller for managing project resources (apps, flows, runners, custom steps).
/// Provides endpoints for linking and unlinking resources to/from projects.
/// </summary>
[Authorize]
[Route("api/projects/{projectId}")]
public class ProjectResourcesController : BaseController
{
    private readonly ProjectResourceService resourceService;
    private readonly ILogger<ProjectResourcesController> logger;

    public ProjectResourcesController(
        ProjectResourceService resourceService,
        ILogger<ProjectResourcesController> logger)
    {
        this.resourceService = resourceService;
        this.logger = logger;
    }

    #region Apps

    /// <summary>
    /// List all apps linked to the project.
    /// Any project member can view project apps.
    /// </summary>
    [HttpGet("apps")]
    [Authorize(Policy = "ProjectMember")]
    public async Task<ActionResult<IEnumerable<ProjectAppDto>>> ListProjectApps(Guid projectId)
    {
        try
        {
            var apps = await resourceService.ListProjectAppsAsync(projectId);
            return Ok(apps);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing apps for project {ProjectId}", projectId);
            return StatusCode(500, new { message = "Error retrieving apps" });
        }
    }

    /// <summary>
    /// Link an app to the project.
    /// Only ProjectAdmin and Developer can link apps.
    /// The app's scope is automatically set to Project and projectId is assigned.
    /// </summary>
    [HttpPost("apps/{appId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult<ProjectAppDto>> AddAppToProject(Guid projectId, Guid appId)
    {
        try
        {
            var projectApp = await resourceService.AddAppToProjectAsync(projectId, appId);
            
            return Ok(projectApp);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error linking app {AppId} to project {ProjectId}", 
                appId, projectId);
            return StatusCode(500, new { message = "Error linking app" });
        }
    }

    /// <summary>
    /// Unlink an app from the project.
    /// Only ProjectAdmin and Developer can unlink apps.
    /// The app's scope is set back to Personal.
    /// </summary>
    [HttpDelete("apps/{appId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult> RemoveAppFromProject(Guid projectId, Guid appId)
    {
        try
        {
            var success = await resourceService.RemoveAppFromProjectAsync(projectId, appId);
            
            if (!success)
            {
                return NotFound(new { message = "App not found in project" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unlinking app {AppId} from project {ProjectId}", 
                appId, projectId);
            return StatusCode(500, new { message = "Error unlinking app" });
        }
    }

    #endregion

    #region Flows

    /// <summary>
    /// List all flows linked to the project.
    /// Any project member can view project flows.
    /// </summary>
    [HttpGet("flows")]
    [Authorize(Policy = "ProjectMember")]
    public async Task<ActionResult<IEnumerable<ProjectFlowDto>>> ListProjectFlows(Guid projectId)
    {
        try
        {
            var flows = await resourceService.ListProjectFlowsAsync(projectId);
            return Ok(flows);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing flows for project {ProjectId}", projectId);
            return StatusCode(500, new { message = "Error retrieving flows" });
        }
    }

    /// <summary>
    /// Link a flow to the project.
    /// Only ProjectAdmin and Developer can link flows.
    /// </summary>
    [HttpPost("flows/{flowId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult<ProjectFlowDto>> AddFlowToProject(Guid projectId, Guid flowId)
    {
        try
        {
            var projectFlow = await resourceService.AddFlowToProjectAsync(projectId, flowId);
            
            return Ok(projectFlow);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error linking flow {FlowId} to project {ProjectId}", 
                flowId, projectId);
            return StatusCode(500, new { message = "Error linking flow" });
        }
    }

    /// <summary>
    /// Unlink a flow from the project.
    /// Only ProjectAdmin and Developer can unlink flows.
    /// </summary>
    [HttpDelete("flows/{flowId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult> RemoveFlowFromProject(Guid projectId, Guid flowId)
    {
        try
        {
            var success = await resourceService.RemoveFlowFromProjectAsync(projectId, flowId);
            
            if (!success)
            {
                return NotFound(new { message = "Flow not found in project" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unlinking flow {FlowId} from project {ProjectId}", 
                flowId, projectId);
            return StatusCode(500, new { message = "Error unlinking flow" });
        }
    }

    #endregion

    #region Runners

    /// <summary>
    /// List all runners linked to the project.
    /// Any project member can view project runners.
    /// </summary>
    [HttpGet("runners")]
    [Authorize(Policy = "ProjectMember")]
    public async Task<ActionResult<IEnumerable<ProjectRunnerDto>>> ListProjectRunners(Guid projectId)
    {
        try
        {
            var runners = await resourceService.ListProjectRunnersAsync(projectId);
            return Ok(runners);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing runners for project {ProjectId}", projectId);
            return StatusCode(500, new { message = "Error retrieving runners" });
        }
    }

    /// <summary>
    /// Link a runner to the project.
    /// Only ProjectAdmin and Developer can link runners.
    /// </summary>
    [HttpPost("runners/{runnerId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult<ProjectRunnerDto>> AddRunnerToProject(Guid projectId, Guid runnerId)
    {
        try
        {
            var projectRunner = await resourceService.AddRunnerToProjectAsync(projectId, runnerId);
            
            return Ok(projectRunner);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error linking runner {RunnerId} to project {ProjectId}", 
                runnerId, projectId);
            return StatusCode(500, new { message = "Error linking runner" });
        }
    }

    /// <summary>
    /// Unlink a runner from the project.
    /// Only ProjectAdmin and Developer can unlink runners.
    /// </summary>
    [HttpDelete("runners/{runnerId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult> RemoveRunnerFromProject(Guid projectId, Guid runnerId)
    {
        try
        {
            var success = await resourceService.RemoveRunnerFromProjectAsync(projectId, runnerId);
            
            if (!success)
            {
                return NotFound(new { message = "Runner not found in project" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unlinking runner {RunnerId} from project {ProjectId}", 
                runnerId, projectId);
            return StatusCode(500, new { message = "Error unlinking runner" });
        }
    }

    #endregion

    #region Custom Steps

    /// <summary>
    /// List all custom steps linked to the project.
    /// Any project member can view project custom steps.
    /// </summary>
    [HttpGet("customsteps")]
    [Authorize(Policy = "ProjectMember")]
    public async Task<ActionResult<IEnumerable<ProjectCustomStepDto>>> ListProjectCustomSteps(Guid projectId)
    {
        try
        {
            var steps = await resourceService.ListProjectCustomStepsAsync(projectId);
            return Ok(steps);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing custom steps for project {ProjectId}", projectId);
            return StatusCode(500, new { message = "Error retrieving custom steps" });
        }
    }

    /// <summary>
    /// Link a custom step to the project.
    /// Only ProjectAdmin and Developer can link custom steps.
    /// </summary>
    [HttpPost("customsteps/{stepId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult<ProjectCustomStepDto>> AddCustomStepToProject(Guid projectId, Guid stepId)
    {
        try
        {
            var projectStep = await resourceService.AddCustomStepToProjectAsync(projectId, stepId);
            
            return Ok(projectStep);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error linking custom step {StepId} to project {ProjectId}", 
                stepId, projectId);
            return StatusCode(500, new { message = "Error linking custom step" });
        }
    }

    /// <summary>
    /// Unlink a custom step from the project.
    /// Only ProjectAdmin and Developer can unlink custom steps.
    /// </summary>
    [HttpDelete("customsteps/{stepId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult> RemoveCustomStepFromProject(Guid projectId, Guid stepId)
    {
        try
        {
            var success = await resourceService.RemoveCustomStepFromProjectAsync(projectId, stepId);
            
            if (!success)
            {
                return NotFound(new { message = "Custom step not found in project" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unlinking custom step {StepId} from project {ProjectId}", 
                stepId, projectId);
            return StatusCode(500, new { message = "Error unlinking custom step" });
        }
    }

    #endregion

    #region API Keys

    /// <summary>
    /// List all API keys linked to the project.
    /// Any project member can view project API keys.
    /// </summary>
    [HttpGet("apikeys")]
    [Authorize(Policy = "ProjectMember")]
    public async Task<ActionResult<IEnumerable<ProjectApiKeyDto>>> ListProjectApiKeys(Guid projectId)
    {
        try
        {
            var apiKeys = await resourceService.ListProjectApiKeysAsync(projectId);
            return Ok(apiKeys);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing API keys for project {ProjectId}", projectId);
            return StatusCode(500, new { message = "Error retrieving API keys" });
        }
    }

    /// <summary>
    /// Link an API key to the project.
    /// Only ProjectAdmin and Developer can link API keys.
    /// </summary>
    [HttpPost("apikeys/{apiKeyId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult<ProjectApiKeyDto>> AddApiKeyToProject(Guid projectId, Guid apiKeyId)
    {
        try
        {
            var projectApiKey = await resourceService.AddApiKeyToProjectAsync(projectId, apiKeyId);
            
            return Ok(projectApiKey);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error linking API key {ApiKeyId} to project {ProjectId}", 
                apiKeyId, projectId);
            return StatusCode(500, new { message = "Error linking API key" });
        }
    }

    /// <summary>
    /// Unlink an API key from the project.
    /// Only ProjectAdmin and Developer can unlink API keys.
    /// </summary>
    [HttpDelete("apikeys/{apiKeyId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult> RemoveApiKeyFromProject(Guid projectId, Guid apiKeyId)
    {
        try
        {
            var success = await resourceService.RemoveApiKeyFromProjectAsync(projectId, apiKeyId);
            
            if (!success)
            {
                return NotFound(new { message = "API key not found in project" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unlinking API key {ApiKeyId} from project {ProjectId}", 
                apiKeyId, projectId);
            return StatusCode(500, new { message = "Error unlinking API key" });
        }
    }

    #endregion

    #region AI API Keys

    /// <summary>
    /// List all AI API keys linked to the project.
    /// Any project member can view project AI API keys.
    /// </summary>
    [HttpGet("aiapikeys")]
    [Authorize(Policy = "ProjectMember")]
    public async Task<ActionResult<IEnumerable<ProjectAiApiKeyDto>>> ListProjectAiApiKeys(Guid projectId)
    {
        try
        {
            var aiApiKeys = await resourceService.ListProjectAiApiKeysAsync(projectId);
            return Ok(aiApiKeys);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing AI API keys for project {ProjectId}", projectId);
            return StatusCode(500, new { message = "Error retrieving AI API keys" });
        }
    }

    /// <summary>
    /// Link an AI API key to the project.
    /// Only ProjectAdmin and Developer can link AI API keys.
    /// </summary>
    [HttpPost("aiapikeys/{aiApiKeyId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult<ProjectAiApiKeyDto>> AddAiApiKeyToProject(Guid projectId, Guid aiApiKeyId)
    {
        try
        {
            var projectAiApiKey = await resourceService.AddAiApiKeyToProjectAsync(projectId, aiApiKeyId);
            
            return Ok(projectAiApiKey);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error linking AI API key {AiApiKeyId} to project {ProjectId}", 
                aiApiKeyId, projectId);
            return StatusCode(500, new { message = "Error linking AI API key" });
        }
    }

    /// <summary>
    /// Unlink an AI API key from the project.
    /// Only ProjectAdmin and Developer can unlink AI API keys.
    /// </summary>
    [HttpDelete("aiapikeys/{aiApiKeyId}")]
    [Authorize(Policy = "ProjectResourceManager")]
    public async Task<ActionResult> RemoveAiApiKeyFromProject(Guid projectId, Guid aiApiKeyId)
    {
        try
        {
            var success = await resourceService.RemoveAiApiKeyFromProjectAsync(projectId, aiApiKeyId);
            
            if (!success)
            {
                return NotFound(new { message = "AI API key not found in project" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unlinking AI API key {AiApiKeyId} from project {ProjectId}", 
                aiApiKeyId, projectId);
            return StatusCode(500, new { message = "Error unlinking AI API key" });
        }
    }

    #endregion
}
