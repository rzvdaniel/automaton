using Automaton.Api.ServerHealth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>How much disk an app uses on its Runner's machine, for its owner, people who may update it, and administrators.</summary>
[ApiController]
[Route("api/apps/{id:guid}/disk")]
[Authorize(Policy = "AllowApiKey")]
public sealed class AppDiskController(AppDiskUsageService disk) : ControllerBase
{
    /// <summary>200 with the breakdown, 204 while no Runner has measured the app yet.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid id, CancellationToken token)
    {
        try
        {
            var usage = await disk.GetAsync(id, User.IsInRole("Administrators"), token);
            return usage is null ? NoContent() : Ok(usage);
        }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (UnauthorizedAccessException exception) { return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message }); }
    }
}
