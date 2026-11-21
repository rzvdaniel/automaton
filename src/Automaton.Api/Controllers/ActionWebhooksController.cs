using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>Owners manage an app's Action webhooks here. The secret is returned only when it is created or rotated.</summary>
[ApiController]
[Authorize(Policy = "AllowApiKey")]
[TypeFilter<ActionScheduleExceptionFilter>]
[Route("api/apps/{appId:guid}/action-webhooks")]
public sealed class ActionWebhooksController(ActionWebhookService webhooks, UserContextService users) : ControllerBase
{
    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    [HttpGet] public async Task<IActionResult> List(Guid appId, CancellationToken ct)
        => Ok(await webhooks.ListAsync(appId, users.GetUserId(), BaseUrl, ct));

    [HttpPost] public async Task<IActionResult> Create(Guid appId, [FromBody] CreateActionWebhookRequest request, CancellationToken ct)
        => Ok(await webhooks.CreateAsync(appId, request, users.GetUserId(), BaseUrl, ct));

    [HttpPost("{id:guid}/enable")] public async Task<IActionResult> Enable(Guid appId, Guid id, CancellationToken ct)
        => Ok(await webhooks.SetEnabledAsync(appId, id, true, users.GetUserId(), BaseUrl, ct));

    [HttpPost("{id:guid}/disable")] public async Task<IActionResult> Disable(Guid appId, Guid id, CancellationToken ct)
        => Ok(await webhooks.SetEnabledAsync(appId, id, false, users.GetUserId(), BaseUrl, ct));

    [HttpPost("{id:guid}/rotate")] public async Task<IActionResult> Rotate(Guid appId, Guid id, CancellationToken ct)
        => Ok(await webhooks.RotateSecretAsync(appId, id, users.GetUserId(), BaseUrl, ct));

    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid appId, Guid id, CancellationToken ct)
    {
        await webhooks.DeleteAsync(appId, id, users.GetUserId(), ct);
        return NoContent();
    }
}
