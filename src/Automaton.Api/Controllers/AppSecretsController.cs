using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

public sealed record SetAppSecretRequest(string Value);

/// <summary>Write-only app secrets: names and versions are listed, values are never returned.</summary>
[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/apps/{appId:guid}/secrets")]
public sealed class AppSecretsController(AppSecretService secrets, UserContextService users) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid appId, CancellationToken token)
        => await Run(async () => Ok(await secrets.ListAsync(appId, users.GetUserId(), token)));

    [HttpPut("{name}")]
    public async Task<IActionResult> Set(Guid appId, string name, [FromBody] SetAppSecretRequest request, CancellationToken token)
        => await Run(async () => Ok(await secrets.SetAsync(appId, users.GetUserId(), name, request.Value, token)));

    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(Guid appId, string name, CancellationToken token)
        => await Run(async () => await secrets.DeleteAsync(appId, users.GetUserId(), name, token) ? NoContent() : NotFound());

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }
}
