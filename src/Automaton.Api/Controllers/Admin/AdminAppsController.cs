using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/apps")]
[Authorize(Policy = "Administrators")]
public class AdminAppsController : ControllerBase
{
    private readonly AdminAppsService adminAppsService;
    private readonly AdminAppDirectoryService directory;

    public AdminAppsController(AdminAppsService adminAppsService, AdminAppDirectoryService directory)
    {
        this.adminAppsService = adminAppsService;
        this.directory = directory;
    }

    /// <summary>Every app with its owner; <c>waiting=true</c> keeps apps with permission requests to decide.</summary>
    [HttpGet]
    public async Task<ActionResult<AdminAppListResponse>> List([FromQuery] string? search, [FromQuery] bool waiting = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Ok(await directory.ListAsync(search, waiting, page, pageSize, cancellationToken));

    [HttpGet("{appId:guid}")]
    public async Task<ActionResult<AdminAppDetails>> Get(Guid appId, CancellationToken cancellationToken)
    {
        var details = await adminAppsService.GetDetailsForAdminAsync(appId, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        return Ok(details);
    }

    [HttpGet("{appId:guid}/filesystem/root")]
    public async Task<IActionResult> GetRootFiles(Guid appId, CancellationToken cancellationToken)
    {
        try
        {
            var files = await adminAppsService.GetRootEntriesForAdminAsync(appId, cancellationToken);
            return Ok(new { success = true, editable = false, files });
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { success = false, message = "App not found" });
        }
    }

    [HttpGet("{appId:guid}/filesystem/root/{*path}")]
    public async Task<IActionResult> ReadRootFile(Guid appId, string path, CancellationToken cancellationToken)
    {
        try
        {
            var content = await adminAppsService.ReadRootFileForAdminAsync(appId, path, cancellationToken);
            if (content is null)
            {
                return NotFound(new { success = false, message = "File not found" });
            }

            return Ok(new { success = true, editable = false, content });
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { success = false, message = "App not found" });
        }
    }

    [HttpPost("{appId:guid}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid appId, CancellationToken cancellationToken)
    {
        try
        {
            await adminAppsService.UnpublishForAdminAsync(appId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { message = "App not found" });
        }
    }

    [HttpPut("{appId:guid}/moderation")]
    public async Task<IActionResult> UpdateModeration(Guid appId, [FromBody] AdminMarketplaceModerationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await adminAppsService.UpdateModerationForAdminAsync(appId, request, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { message = "App not found" });
        }
    }
}
