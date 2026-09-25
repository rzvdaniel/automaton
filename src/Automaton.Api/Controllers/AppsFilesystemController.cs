using Automaton.Api.Constants;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/apps")]
public sealed class AppsFilesystemController(
    AppsService appsService,
    UserContextService userContextService,
    AppFileStorageService fileStorageService,
    ILogger<AppsFilesystemController> logger) : ControllerBase
{
    [HttpGet("{id}/filesystem")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> GetAppFilesystem(Guid id, [FromQuery] string scope = "source")
    {
        try
        {
            var appInfo = await appsService.GetAccessibleAppInfoByIdAsync(id);
            if (appInfo is null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            var normalizedScope = NormalizeFileSystemScope(scope);
            if (normalizedScope is null)
            {
                return BadRequest(new { success = false, message = "Invalid filesystem scope" });
            }

            var files = await fileStorageService.GetSourceFilesAsync(ownerId, id);
            return Ok(new { success = true, scope = normalizedScope, editable = true, files = files.ToList() });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to get filesystem for app {AppId}", id);
            return StatusCode(500, new { success = false, message = exception.Message });
        }
    }

    /// <summary>
    /// Read content of a source file through the scoped CLI filesystem API.
    /// </summary>
    [HttpGet("{id}/filesystem/{scope}/{*path}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> GetAppFilesystemFile(Guid id, string scope, string path)
    {
        try
        {
            var appInfo = await appsService.GetAccessibleAppInfoByIdAsync(id);
            if (appInfo is null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            var normalizedScope = NormalizeFileSystemScope(scope);
            if (normalizedScope is null)
            {
                return BadRequest(new { success = false, message = "Invalid filesystem scope" });
            }

            var content = await fileStorageService.ReadSourceFileAsync(ownerId, id, path);
            if (content is null)
            {
                return NotFound(new { success = false, message = "File not found" });
            }

            return Ok(new { success = true, scope = normalizedScope, editable = true, content });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read filesystem file {Path} for app {AppId}", path, id);
            return StatusCode(500, new { success = false, message = exception.Message });
        }
    }

    private static string? NormalizeFileSystemScope(string? scope)
        => scope?.Trim().ToLowerInvariant() switch
        {
            AppFileSystemScopes.Source => AppFileSystemScopes.Source,
            _ => null
        };
}
