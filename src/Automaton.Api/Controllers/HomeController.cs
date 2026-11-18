using Automaton.Api.Home;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>Studio Home for the signed-in person. Pins and dismissals belong to that person alone.</summary>
[ApiController]
[Route("api/home")]
[Authorize(Policy = "DeveloperApiAccess")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HomeController(HomeService home) : BaseController
{
    [HttpGet]
    public async Task<ActionResult<HomeResponse>> Get(CancellationToken token)
        => GetUserId() is var userId && userId != Guid.Empty ? Ok(await home.GetAsync(userId, token)) : Unauthorized();

    [HttpPut("pins/{appId:guid}")]
    public async Task<IActionResult> Pin(Guid appId, CancellationToken token)
        => GetUserId() is var userId && userId != Guid.Empty
            ? await home.PinAsync(userId, appId, token) ? NoContent() : NotFound()
            : Unauthorized();

    /// <summary>Runs the app's card once now. 429 with Retry-After when it was refreshed less than a minute ago.</summary>
    [HttpPost("cards/{appId:guid}/refresh")]
    public async Task<IActionResult> RefreshCard(Guid appId, CancellationToken token)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();
        var (outcome, retryAfter) = await home.RefreshCardAsync(userId, appId, User.IsInRole("Administrators"), token);
        switch (outcome)
        {
            case HomeService.RefreshOutcome.Started: return Accepted();
            case HomeService.RefreshOutcome.NotFound: return NotFound(new { message = "App not found." });
            case HomeService.RefreshOutcome.Forbidden: return StatusCode(StatusCodes.Status403Forbidden, new { message = "You do not have permission to refresh this card." });
            case HomeService.RefreshOutcome.NoCard: return Conflict(new { message = "This app has no card to refresh." });
            default:
                var seconds = (int)Math.Ceiling(retryAfter!.Value.TotalSeconds);
                Response.Headers.RetryAfter = seconds.ToString();
                return StatusCode(StatusCodes.Status429TooManyRequests, new { message = $"This card was just refreshed. Try again in {seconds} seconds.", retryAfterSeconds = seconds });
        }
    }

    [HttpDelete("pins/{appId:guid}")]
    public async Task<IActionResult> Unpin(Guid appId, CancellationToken token)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await home.UnpinAsync(userId, appId, token);
        return NoContent();
    }

    [HttpPut("pins/order")]
    public async Task<IActionResult> Reorder([FromBody] HomePinOrderRequest request, CancellationToken token)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();
        return await home.ReorderAsync(userId, request.AppIds ?? [], token) ? NoContent() : BadRequest(new { message = "Send every pinned app once, in the new order." });
    }

    [HttpPut("dismissals/{cardKey}")]
    public async Task<IActionResult> Dismiss(string cardKey, CancellationToken token)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();
        return await home.DismissAsync(userId, cardKey, token) ? NoContent() : NotFound();
    }
}
