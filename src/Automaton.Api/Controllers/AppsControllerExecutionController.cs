using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>Generic application deployment endpoints.</summary>
[ApiController]
public sealed class AppsControllerExecutionController(
    AppsService appsService,
    UserContextService userContextService,
    ConfirmedEmailGuardService confirmedEmailGuardService,
    IAppDeployCapabilityService appDeployCapabilityService,
    ManagedRunnerBootstrapService managedRunnerBootstrapService,
    DeploymentOperationService deploymentOperationService) : ControllerBase
{
    [HttpPost("api/apps/{appId:guid}/managed-runtime/readiness")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<ManagedRunnerBootstrapResult>> GetManagedRuntimeReadiness(
        Guid appId, CancellationToken cancellationToken) =>
        Ok(await managedRunnerBootstrapService.EnsureRunnerAssignmentAsync(
            appId, userContextService.GetUserId(), cancellationToken));

    [HttpGet("api/apps/{appId:guid}/deploy-capabilities")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<AppDeployCapabilitiesResponse>> GetDeployCapabilities(
        Guid appId, CancellationToken cancellationToken)
    {
        var appInfo = await appsService.GetAccessibleAppInfoByIdAsync(appId, cancellationToken);
        if (appInfo == null)
            return NotFound(new { message = "App not found" });

        var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
        return Ok(await appDeployCapabilityService.GetCapabilitiesAsync(ownerId, appId, cancellationToken));
    }

    [HttpPost("api/apps/{appId:guid}/deploy-package")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<DeploymentOperationResponse>> DeployPackage(
        Guid appId, [FromForm] IFormFile? file, [FromQuery] bool clean = false,
        [FromQuery] string? targetSlug = null, CancellationToken cancellationToken = default)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = ConfirmedEmailGuardService.SensitiveActionMessage });

        try
        {
            var result = await deploymentOperationService.AcceptAsync(
                appId, userContextService.GetUserId(), targetSlug, file, clean, cancellationToken);
            return AcceptedAtAction(nameof(GetDeploymentOperation),
                new { appId, operationId = result.OperationId }, result);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Deploy package failed: AppId={AppId}", appId);
            return StatusCode(500, new { error = "Deploy package failed: " + ex.Message });
        }
    }

    [HttpGet("api/apps/{appId:guid}/deploy-operations/{operationId:guid}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<DeploymentOperationResponse>> GetDeploymentOperation(
        Guid appId, Guid operationId, CancellationToken cancellationToken)
    {
        var result = await deploymentOperationService.GetAsync(
            appId, userContextService.GetUserId(), operationId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
