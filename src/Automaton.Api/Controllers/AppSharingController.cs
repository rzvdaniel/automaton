using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

public sealed record SetAppSharingRequest(string Audience, string? PublicUrl);

public sealed record AddAppSharePersonRequest(string Email);

/// <summary>Who can open an app. Studio's Share dialog and the CLI's <c>automaton share</c> use these endpoints.</summary>
[Route("api/apps/{id:guid}/sharing")]
public class AppSharingController(
    AppSharingService sharingService,
    UserContextService userContextService,
    ConfirmedEmailGuardService confirmedEmailGuardService) : BaseController
{
    [HttpGet]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        RunAsync(() => sharingService.GetAsync(id, cancellationToken), requireConfirmedEmail: false);

    [HttpPut]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Set(Guid id, [FromBody] SetAppSharingRequest request, CancellationToken cancellationToken) =>
        RunAsync(() => sharingService.SetAudienceAsync(id, request.Audience, request.PublicUrl, cancellationToken));

    [HttpPost("people")]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> AddPerson(Guid id, [FromBody] AddAppSharePersonRequest request, CancellationToken cancellationToken) =>
        RunAsync(() => sharingService.AddPersonAsync(id, request.Email, cancellationToken));

    [HttpDelete("people/{userId:guid}")]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> RemovePerson(Guid id, Guid userId, CancellationToken cancellationToken) =>
        RunAsync(() => sharingService.RemovePersonAsync(id, userId, cancellationToken));

    private async Task<IActionResult> RunAsync(Func<Task<AppSharingState>> action, bool requireConfirmedEmail = true)
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
