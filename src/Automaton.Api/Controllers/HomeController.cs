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
