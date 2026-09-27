using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/apps")]
public sealed class AppsArchiveController(
    AppArchiveService appArchiveService,
    AppRuntimeArchiveUploadService runtimeArchiveUploadService,
    ILogger<AppsArchiveController> logger) : ControllerBase
{
    /// <summary>
    /// Export an app as a portable zip bundle with transferable metadata.
    /// </summary>
    [HttpGet("{id}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var archive = await appArchiveService.ExportAsync(id, cancellationToken);
            return archive is null
                ? NotFound(new { message = "App not found" })
                : File(archive.Content, "application/zip", archive.FileName);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error exporting app {AppId}", id);
            return StatusCode(500, new { message = "Failed to export app" });
        }
    }

    /// <summary>
    /// Import an exported app bundle into the current user's account.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(52_428_800)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<AppModel>> Import(IFormFile file, CancellationToken cancellationToken)
    {
        try
        {
            if (file is null || file.Length == 0)
            {
                return BadRequest(new { message = "No file uploaded or file is empty" });
            }

            if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Only .zip archives are supported" });
            }

            await using var stream = file.OpenReadStream();
            var result = await appArchiveService.ImportAsync(stream, file.FileName, cancellationToken);
            return CreatedAtAction("Get", "Apps", new { id = result.App.Id }, result.App);
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (AppTagValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error importing app archive {FileName}", file?.FileName);
            return StatusCode(500, new { message = "Failed to import app" });
        }
    }

    /// <summary>
    /// Upload deployed live files as a zip archive. Use <c>?clean=true</c> for a full replace.
    /// </summary>
    [HttpPost("{id}/upload")]
    [Authorize(Policy = "AllowApiKey")]
    [RequestSizeLimit(52_428_800)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadZip(
        Guid id,
        IFormFile file,
        [FromQuery] bool clean = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (file is null || file.Length == 0)
            {
                return BadRequest(new { success = false, message = "No file uploaded or file is empty" });
            }

            if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { success = false, message = "Only .zip archives are supported" });
            }

            var result = await runtimeArchiveUploadService.UploadAsync(id, file, clean, cancellationToken);
            if (result is null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            return Ok(new { success = true, result.Written, result.Skipped, result.Errors });
        }
        catch (InvalidDataException)
        {
            return BadRequest(new { success = false, message = "Invalid or corrupt zip archive" });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to upload zip for app {AppId}", id);
            return StatusCode(500, new { success = false, message = exception.Message });
        }
    }
}
