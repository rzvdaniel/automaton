using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

public sealed record AddAppCustomDomainRequest(string Host);

/// <summary>An app's own domains, such as blog.example.com: add, check DNS, choose the primary one, remove.</summary>
[Route("api/apps/{id:guid}/domains")]
public class AppCustomDomainsController(
    CustomDomainService domainService,
    UserContextService userContextService,
    ConfirmedEmailGuardService confirmedEmailGuardService) : BaseController
{
    [HttpGet]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        RunAsync(() => domainService.GetAsync(id, cancellationToken), requireConfirmedEmail: false);

    [HttpPost]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Add(Guid id, [FromBody] AddAppCustomDomainRequest request, CancellationToken cancellationToken) =>
        RunAsync(() => domainService.AddAsync(id, request.Host, cancellationToken));

    [HttpPost("{domainId:guid}/verify")]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Verify(Guid id, Guid domainId, CancellationToken cancellationToken) =>
        RunAsync(() => domainService.VerifyAsync(id, domainId, cancellationToken));

    [HttpPut("{domainId:guid}/primary")]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> SetPrimary(Guid id, Guid domainId, CancellationToken cancellationToken) =>
        RunAsync(() => domainService.SetPrimaryAsync(id, domainId, cancellationToken));

    [HttpDelete("{domainId:guid}")]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Remove(Guid id, Guid domainId, CancellationToken cancellationToken) =>
        RunAsync(() => domainService.RemoveAsync(id, domainId, cancellationToken));

    private async Task<IActionResult> RunAsync(Func<Task<AppCustomDomainsState>> action, bool requireConfirmedEmail = true)
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
