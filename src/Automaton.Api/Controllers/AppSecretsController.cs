using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

public sealed record SetAppSecretRequest(string Value, string? Description = null);

public sealed record AppSecretStatusRequest(List<ApplicationSecretDeclaration> Secrets);

/// <summary>
/// Write-only secrets, addressed relative to an app: its own (<c>scope=app</c>, the default), its project's
/// vault (<c>scope=project</c>) or the caller's vault (<c>scope=user</c>). Names and versions are listed;
/// values are never returned.
/// </summary>
[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/apps/{appId:guid}/secrets")]
public sealed class AppSecretsController(AppSecretService secrets, UserContextService users) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid appId, [FromQuery] string scope = AppSecretScopes.App, CancellationToken token = default)
        => await Run(async () => Ok(await secrets.ListAsync(appId, users.GetUserId(), scope, token)));

    [HttpPut("{name}")]
    public async Task<IActionResult> Set(Guid appId, string name, [FromBody] SetAppSecretRequest request,
        [FromQuery] string scope = AppSecretScopes.App, CancellationToken token = default)
        => await Run(async () => Ok(await secrets.SetAsync(appId, users.GetUserId(), scope, name, request.Value, request.Description, token)));

    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(Guid appId, string name, [FromQuery] string scope = AppSecretScopes.App,
        CancellationToken token = default)
        => await Run(async () => await secrets.DeleteAsync(appId, users.GetUserId(), scope, name, token) ? NoContent() : NotFound());

    /// <summary>Whether each declared secret is available to the app, and from which scope.</summary>
    [HttpPost("status")]
    public async Task<IActionResult> Status(Guid appId, [FromBody] AppSecretStatusRequest request, CancellationToken token)
        => await Run(async () => Ok(await secrets.StatusAsync(appId, users.GetUserId(), request.Secrets ?? [], token)));

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }
}
