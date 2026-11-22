using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

public sealed record SetAppSettingValueRequest(string Value);

/// <summary>Readable values of the plain settings an app declares. Secrets are never available here.</summary>
[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/apps/{appId:guid}/setting-values")]
public sealed class AppSettingValuesController(AppSettingValueService settings, UserContextService users) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid appId, CancellationToken token)
        => await Run(async () => Ok(await settings.ListAsync(appId, users.GetUserId(), token)));

    [HttpPut("{name}")]
    public async Task<IActionResult> Set(Guid appId, string name, [FromBody] SetAppSettingValueRequest request, CancellationToken token)
        => await Run(async () => Ok(await settings.SetAsync(appId, users.GetUserId(), name, request.Value, token)));

    [HttpDelete("{name}")]
    public async Task<IActionResult> Clear(Guid appId, string name, CancellationToken token)
        => await Run(async () => await settings.ClearAsync(appId, users.GetUserId(), name, token) ? NoContent() : NotFound());

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }
}
