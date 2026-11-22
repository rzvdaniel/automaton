using Automaton.Api.Authentication;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Lets an app with an approved <c>notify:email</c> permission email its owners, the server's administrators, its
/// project's members and administrator-approved addresses through the server's SMTP. Administrators and API keys get 403.
/// </summary>
[ApiController]
[Route("api/notify/email")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.NotifyEmailPolicy)]
public sealed class NotifyEmailController(NotifyEmailService notify) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send(NotifyEmailRequest request, CancellationToken token)
    {
        var appId = Guid.Parse(User.FindFirst("app_id")!.Value);
        try { return Ok(await notify.SendAsync(appId, request, token)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (NotifyEmailException error)
        {
            if (error.RetryAfterSeconds is { } seconds) Response.Headers.RetryAfter = seconds.ToString();
            var message = new { message = error.Message };
            return error.Failure switch
            {
                NotifyEmailFailure.Invalid => BadRequest(message),
                NotifyEmailFailure.NotAllowed => StatusCode(StatusCodes.Status403Forbidden, message),
                NotifyEmailFailure.NoRecipients => NotFound(message),
                NotifyEmailFailure.RateLimited => StatusCode(StatusCodes.Status429TooManyRequests, message),
                _ => Conflict(message)
            };
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "The SMTP server did not accept the message." });
        }
    }
}
