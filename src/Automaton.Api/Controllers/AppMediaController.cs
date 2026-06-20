using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/apps/{appId:guid}/media")]
[Authorize]
public class AppMediaController : ControllerBase
{
    private readonly ApplicationDbContext dataContext;
    private readonly AppMediaStorageService mediaStorageService;

    public AppMediaController(
        ApplicationDbContext dataContext,
        AppMediaStorageService mediaStorageService)
    {
        this.dataContext = dataContext;
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

        var app = await dataContext.WebApps
            .Include(a => a.WebAppUsers)
            .FirstOrDefaultAsync(a => a.Id == appId && a.WebAppUsers.Any(x => x.UserId == userId), ct);

        if (app is null)
        {
            return NotFound(new { error = "App not found." });
        }

        var ownerId = app.WebAppUsers.Select(x => x.UserId).FirstOrDefault();
        if (ownerId == Guid.Empty)
        {
            return BadRequest(new { error = "App has no owner configured." });
        }

        var result = await mediaStorageService.UploadAsync(appId, ownerId, file, ct);
        return StatusCode(result.StatusCode, result.Payload);
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
                 ?? User.Claims.FirstOrDefault(c => c.Type == "uid");
        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }
}
