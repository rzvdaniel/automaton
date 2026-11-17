using Automaton.Api.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>The notification feed for the signed-in person. Studio, the CLI and agents with an API key read the same list.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize(Policy = "DeveloperApiAccess")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(NotificationService notifications) : BaseController
{
    [HttpGet]
    public async Task<ActionResult<NotificationListResponse>> Get(CancellationToken token)
        => GetUserId() is var userId && userId != Guid.Empty ? Ok(new NotificationListResponse(await notifications.ListAsync(userId, token))) : Unauthorized();

    [HttpPut("{key}/acknowledgement")]
    public async Task<IActionResult> Acknowledge(string key, CancellationToken token)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();
        return await notifications.AcknowledgeAsync(userId, key, token) switch
        {
            NotificationService.AcknowledgeResult.Done => NoContent(),
            NotificationService.AcknowledgeResult.Critical => Conflict(new { message = "A critical notification stays until the cause is fixed." }),
            _ => NotFound(),
        };
    }
}
