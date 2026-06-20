using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/apps/{appId:guid}/media")]
[Authorize]
public class AppMediaController : ControllerBase
{
    private readonly AppsService appsService;
    private readonly AppMediaStorageService mediaStorageService;

    public AppMediaController(
        AppsService appsService,
        AppMediaStorageService mediaStorageService)
    {
        this.appsService = appsService;
        this.mediaStorageService = mediaStorageService;
    }

    [HttpPost]
    public async Task<IActionResult> Upload(Guid appId, IFormFile file, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var app = await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, ct);
        if (app is null)
        {
            return NotFound(new { error = "App not found." });
        }

        if (!app.OwnerId.HasValue)
        {
            return BadRequest(new { error = "App has no owner configured." });
        }

        var result = await mediaStorageService.UploadAsync(appId, app.OwnerId.Value, file, ct);
        return StatusCode(result.StatusCode, result.Payload);
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
                 ?? User.Claims.FirstOrDefault(c => c.Type == "uid");
        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }
}
