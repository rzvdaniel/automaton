using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[TypeFilter(typeof(AppActionExceptionFilter))]
[Authorize(Policy = "AllowApiKey")]
[Route("api/apps/{appId:guid}")]
public sealed class AppActionsController : ControllerBase
{
    private readonly AppActionExecutionService executions;
    private readonly UserContextService userContext;

    public AppActionsController(AppActionExecutionService executions, UserContextService userContext)
    {
        this.executions = executions;
        this.userContext = userContext;
    }

    [HttpPost("actions/{actionKey}/executions")]
    public async Task<IActionResult> Execute(Guid appId, string actionKey, [FromBody] RunAppActionRequest? request, CancellationToken cancellationToken)
        => Ok(await executions.ExecuteAsync(appId, actionKey, userContext.GetUserId(), request?.Input ?? default, cancellationToken));

    [HttpGet("actions")]
    public async Task<IActionResult> ListActions(Guid appId, CancellationToken cancellationToken)
        => Ok(await executions.ListModulesAsync(appId, userContext.GetUserId(), cancellationToken));

    [HttpGet("actions/{actionKey}")]
    public async Task<IActionResult> GetAction(Guid appId, string actionKey, CancellationToken cancellationToken)
        => Ok(await executions.GetModuleAsync(appId, actionKey, userContext.GetUserId(), cancellationToken));

    [HttpGet("actions/{actionKey}/versions")]
    public async Task<IActionResult> Versions(Guid appId, string actionKey, CancellationToken cancellationToken)
        => Ok(await executions.ListVersionsAsync(appId, actionKey, userContext.GetUserId(), cancellationToken));

    [HttpPost("actions/{actionKey}/rollback")]
    public async Task<IActionResult> Rollback(Guid appId, string actionKey, [FromBody] AppActionVersionBody request, CancellationToken cancellationToken)
        => Ok(await executions.RestoreVersionAsync(appId, actionKey, request.Version, userContext.GetUserId(), cancellationToken));

    [HttpDelete("actions/{actionKey}/versions/{version}")]
    public async Task<IActionResult> DeleteVersion(Guid appId, string actionKey, string version, CancellationToken cancellationToken)
        => Ok(await executions.DeleteVersionAsync(appId, actionKey, version, userContext.GetUserId(), cancellationToken));

    [HttpGet("action-executions")]
    public async Task<IActionResult> List(Guid appId, CancellationToken cancellationToken)
        => Ok(await executions.ListAsync(appId, userContext.GetUserId(), cancellationToken));

    [HttpGet("action-executions/{executionId:guid}")]
    public async Task<IActionResult> Get(Guid appId, Guid executionId, CancellationToken cancellationToken)
        => Ok(await executions.GetAsync(appId, executionId, userContext.GetUserId(), cancellationToken));

    [HttpPost("action-executions/{executionId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid appId, Guid executionId, CancellationToken cancellationToken)
        => Ok(new { cancelled = await executions.CancelAsync(appId, executionId, userContext.GetUserId(), cancellationToken) });

    [HttpGet("action-executions/{executionId:guid}/logs")]
    public async Task<IActionResult> Logs(Guid appId, Guid executionId, CancellationToken cancellationToken)
        => Ok(await executions.ReadArtifactAsync(appId, executionId, userContext.GetUserId(), result: false, cancellationToken));

    [HttpGet("action-executions/{executionId:guid}/result")]
    public async Task<IActionResult> Result(Guid appId, Guid executionId, CancellationToken cancellationToken)
        => Ok(await executions.ReadArtifactAsync(appId, executionId, userContext.GetUserId(), result: true, cancellationToken));
}

public sealed class RunAppActionRequest
{
    public System.Text.Json.JsonElement Input { get; set; }
}

public sealed record AppActionVersionBody(string Version);
