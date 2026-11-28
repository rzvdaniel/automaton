using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

public sealed record AddAppDataGrantRequest(Guid GranteeAppId, string Volume, bool ReadWrite = false, bool ConfirmReadWrite = false);

/// <summary>Which other apps (admin tools) may open this app's data volumes, and which apps' data this app may open.</summary>
[Route("api/apps/{id:guid}/data-grants")]
public class AppDataGrantsController(
    AppDataGrantService grants,
    UserContextService userContextService,
    ConfirmedEmailGuardService confirmedEmailGuardService) : BaseController
{
    [HttpGet]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        RunAsync(() => grants.GetAsync(id, cancellationToken), requireConfirmedEmail: false);

    [HttpPost]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Add(Guid id, [FromBody] AddAppDataGrantRequest request, CancellationToken cancellationToken) =>
        RunAsync(() => grants.AddAsync(id, request.GranteeAppId, request.Volume, request.ReadWrite, request.ConfirmReadWrite, cancellationToken));

    [HttpDelete("{grantId:guid}")]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Remove(Guid id, Guid grantId, CancellationToken cancellationToken) =>
        RunAsync(() => grants.RemoveAsync(id, grantId, cancellationToken));

    private async Task<IActionResult> RunAsync(Func<Task<AppDataGrantsState>> action, bool requireConfirmedEmail = true)
    {
        if (requireConfirmedEmail && !await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        try
        {
            return Ok(await action());
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
