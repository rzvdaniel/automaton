using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>Lets a Machine Management Runner load the secrets of an app runtime it is assigned to run.</summary>
[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/runner-runtime/apps/{appId:guid}/targets/{deploymentTargetId:guid}/secrets")]
public sealed class RunnerRuntimeSecretsController(
    RunnerExecutionIdentityService identity, AppSecretService secrets) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid appId, Guid deploymentTargetId, CancellationToken token)
    {
        var runnerId = Guid.TryParse(Request.Headers["X-Runner-Id"], out var id) ? id : Guid.Empty;
        var keyId = Guid.TryParse(User.FindFirst("api_key_id")?.Value, out var key) ? key : Guid.Empty;
        var actorId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var actor) ? actor : Guid.Empty;
        try
        {
            await identity.EnsureAsync(keyId, runnerId, actorId, token);
            Response.Headers.CacheControl = "no-store";
            return Ok(await secrets.ResolveForRuntimeAsync(appId, deploymentTargetId, runnerId, token));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
