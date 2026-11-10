using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

public sealed record NotifyRecipientsDocument(IReadOnlyList<string>? Addresses);

/// <summary>The server-wide list of extra addresses apps with <c>notify:email</c> may email.</summary>
[ApiController]
[Route("api/admin/installation/notify-recipients")]
[Authorize(Roles = UserRole.SystemAdministrator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminNotifyRecipientsController(NotifyEmailService notify) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotifyRecipientsDocument>> Get(CancellationToken token)
        => Ok(new NotifyRecipientsDocument(await notify.AllowedAsync(token)));

    [HttpPut]
    public async Task<IActionResult> Put(NotifyRecipientsDocument input, CancellationToken token)
    {
        try { await notify.SetAllowedAsync(input.Addresses, token); }
        catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
        return NoContent();
    }
}
