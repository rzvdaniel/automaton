using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/marketplace/apps/{appId:guid}/star")]
[Authorize]
public class WebAppStarsController : BaseController
{
    private readonly WebAppStarService _webAppStarService;

    public WebAppStarsController(WebAppStarService webAppStarService)
    {
        _webAppStarService = webAppStarService;
    }

    [HttpPost]
    public async Task<IActionResult> Star(Guid appId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var success = await _webAppStarService.AddStarAsync(appId, userId, cancellationToken);
        return success ? NoContent() : NotFound();
    }

    [HttpDelete]
    public async Task<IActionResult> Unstar(Guid appId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        await _webAppStarService.RemoveStarAsync(appId, userId, cancellationToken);
        return NoContent();
    }
}
