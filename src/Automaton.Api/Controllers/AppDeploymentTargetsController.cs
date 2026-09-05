using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/apps/{appId:guid}/targets")]
[Authorize(Policy = "AllowApiKey")]
[ServiceFilter(typeof(AppDeploymentTargetExceptionFilter))]
public sealed class AppDeploymentTargetsController(
    AppDeploymentTargetManagementService targetService,
    UserContextService userContextService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AppDeploymentTargetSummary>>> List(
        Guid appId,
        CancellationToken cancellationToken) =>
        Ok(await targetService.ListAsync(appId, userContextService.GetUserId(), cancellationToken));

    [HttpGet("{targetId:guid}")]
    public async Task<ActionResult<AppDeploymentTargetSummary>> Get(
        Guid appId,
        Guid targetId,
        CancellationToken cancellationToken) =>
        Ok(await targetService.GetAsync(appId, targetId, userContextService.GetUserId(), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<AppDeploymentTargetSummary>> Create(
        Guid appId,
        CreateAppDeploymentTargetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await targetService.CreateAsync(
            appId, request, userContextService.GetUserId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { appId, targetId = result.Id }, result);
    }

    [HttpPatch("{targetId:guid}")]
    public async Task<ActionResult<AppDeploymentTargetSummary>> Update(
        Guid appId,
        Guid targetId,
        UpdateAppDeploymentTargetRequest request,
        CancellationToken cancellationToken) =>
        Ok(await targetService.UpdateAsync(
            appId, targetId, request, userContextService.GetUserId(), cancellationToken));

    [HttpPost("{targetId:guid}/make-default")]
    public async Task<ActionResult<AppDeploymentTargetSummary>> MakeDefault(
        Guid appId,
        Guid targetId,
        CancellationToken cancellationToken) =>
        Ok(await targetService.MakeDefaultAsync(
            appId, targetId, userContextService.GetUserId(), cancellationToken));

    [HttpPost("{targetId:guid}/privileged-host-access")]
    [Authorize(Policy = "Administrators")]
    public async Task<ActionResult<AppDeploymentTargetSummary>> ApprovePrivilegedHostAccess(
        Guid appId,
        Guid targetId,
        CancellationToken cancellationToken) =>
        Ok(await targetService.SetPrivilegedHostApprovalAsync(
            appId, targetId, userContextService.GetUserId(), true, cancellationToken));

    [HttpDelete("{targetId:guid}/privileged-host-access")]
    [Authorize(Policy = "Administrators")]
    public async Task<ActionResult<AppDeploymentTargetSummary>> RevokePrivilegedHostAccess(
        Guid appId,
        Guid targetId,
        CancellationToken cancellationToken) =>
        Ok(await targetService.SetPrivilegedHostApprovalAsync(
            appId, targetId, userContextService.GetUserId(), false, cancellationToken));

    [HttpPost("repair-legacy")]
    public async Task<ActionResult<LegacyDeploymentTargetRepairResult>> RepairLegacy(
        Guid appId,
        [FromQuery] bool dryRun = true,
        CancellationToken cancellationToken = default) =>
        Ok(await targetService.RepairLegacyTargetsAsync(
            appId, userContextService.GetUserId(), dryRun, cancellationToken));

    [HttpPost("{targetId:guid}/assign-runner")]
    public async Task<ActionResult<AppDeploymentTargetSummary>> AssignRunner(
        Guid appId,
        Guid targetId,
        AssignAppDeploymentTargetRunnerRequest request,
        CancellationToken cancellationToken) =>
        Ok(await targetService.AssignRunnerAsync(
            appId, targetId, request, userContextService.GetUserId(), cancellationToken));

    [HttpDelete("{targetId:guid}")]
    public async Task<IActionResult> Delete(
        Guid appId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        await targetService.DeleteAsync(
            appId, targetId, userContextService.GetUserId(), cancellationToken);
        return NoContent();
    }
}
