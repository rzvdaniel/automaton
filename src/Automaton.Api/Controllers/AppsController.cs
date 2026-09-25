using Automaton.Api.Constants;
using Automaton.Api.Config;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.IO.Compression;

namespace Automaton.Api.Controllers;

public class AppsController : BaseController
{
    private readonly AppsService appsService;
    private readonly ILogger<AppsController> logger;
    private readonly UserContextService userContextService;
    private readonly AppVersionService appVersionService;
    private readonly AppRuntimeArchiveUploadService runtimeArchiveUploadService;
    private readonly AppArchiveService appArchiveService;
    private readonly SettingsService settingsService;
    private readonly ConfirmedEmailGuardService confirmedEmailGuardService;
    private readonly PrivateAppLaunchService privateAppLaunchService;
    private readonly RuntimeCookieService runtimeCookieService;
    private readonly PublishedAppsConfig publishedAppsConfig;
    private readonly IApplicationEventService applicationEventService;
    private readonly AppImageManagementService appImageManagementService;

    public AppsController(
        AppsService appsService,
        ILogger<AppsController> logger,
        UserContextService userContextService,
        AppVersionService appVersionService,
        AppRuntimeArchiveUploadService runtimeArchiveUploadService,
        AppArchiveService appArchiveService,
        SettingsService settingsService,
        ConfirmedEmailGuardService confirmedEmailGuardService,
        PrivateAppLaunchService privateAppLaunchService,
        RuntimeCookieService runtimeCookieService,
        IOptions<PublishedAppsConfig> publishedAppsConfig,
        IApplicationEventService applicationEventService,
        AppImageManagementService appImageManagementService)
    {
        this.appsService = appsService;
        this.logger = logger;
        this.userContextService = userContextService;
        this.appVersionService = appVersionService;
        this.runtimeArchiveUploadService = runtimeArchiveUploadService;
        this.appArchiveService = appArchiveService;
        this.settingsService = settingsService;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
        this.privateAppLaunchService = privateAppLaunchService;
        this.runtimeCookieService = runtimeCookieService;
        this.publishedAppsConfig = publishedAppsConfig.Value;
        this.applicationEventService = applicationEventService;
        this.appImageManagementService = appImageManagementService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AppListItemDto>>> Get([FromQuery] Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        return Ok(await appsService.ListAsync(projectId, cancellationToken));
    }

    [HttpGet("tags")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetTagSuggestions(
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        return Ok(await appsService.ListTagSuggestionsAsync(projectId, cancellationToken));
    }

    [HttpGet("{id}/tag-settings")]
    public async Task<ActionResult<AppTagSettingsDto>> GetTagSettings(Guid id, CancellationToken cancellationToken)
    {
        var settings = await appsService.GetTagSettingsAsync(id, cancellationToken);
        return settings is null ? NotFound() : Ok(settings);
    }

    [HttpPut("{id}/tag-settings")]
    public async Task<IActionResult> UpdateTagSettings(
        Guid id,
        UpdateAppTagSettingsRequest request,
        CancellationToken cancellationToken)
    {
        return await appsService.UpdateTagSettingsAsync(id, request.Tags, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "AllowApiKey")]
    public ActionResult<AppModel> Get(Guid id)
    {
        var app = appsService.Get(id);

        if (app is null)
        {
            return NotFound();
        }

        return Ok(app);
    }

    /// <summary>
    /// Get sample apps for dashboard/onboarding (publicly accessible)
    /// </summary>
    [HttpGet("samples")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<AppModel>>> GetSampleApps()
    {
        try
        {
            var samples = await appsService.GetSampleAppsAsync();
            return Ok(samples);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error getting sample apps");
            return StatusCode(500, new { message = "Failed to get sample apps" });
        }
    }

    /// <summary>
    /// Set sample status for an app (admin only)
    /// </summary>
    [HttpPut("{id}/sample")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetSampleStatus(Guid id, [FromBody] SetSampleStatusRequest request)
    {
        try
        {
            await appsService.SetSampleStatusAsync(id, request.IsSample, request.IsFeatured);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error setting sample status for app {AppId}", id);
            return StatusCode(500, new { message = "Failed to set sample status" });
        }
    }

    /// <summary>
    /// Get template apps — same as sample apps, publicly accessible.
    /// </summary>
    [HttpGet("templates")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<AppModel>>> GetTemplates()
    {
        try
        {
            var templates = await appsService.GetSampleAppsAsync();
            return Ok(templates);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error getting template apps");
            return StatusCode(500, new { message = "Failed to get templates" });
        }
    }

    /// <summary>
    /// Duplicate an owned app (copies files + metadata). Requires authentication.
    /// </summary>
    [HttpPost("{id}/duplicate")]
    public async Task<ActionResult<AppModel>> Duplicate(Guid id)
    {
        try
        {
            var newApp = await appsService.DuplicateAsync(id);
            return CreatedAtAction(nameof(Get), new { id = newApp.Id }, newApp);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error duplicating app {AppId}", id);
            return StatusCode(500, new { message = "Failed to duplicate app" });
        }
    }

    /// <summary>
    /// Get the published app metadata needed to confirm a fork.
    /// </summary>
    [HttpGet("{id}/fork-source")]
    public async Task<ActionResult<ForkAppSourceModel>> GetForkSource(Guid id)
    {
        try
        {
            var source = await appsService.GetForkSourceAsync(id);
            return Ok(source);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error loading fork source app {AppId}", id);
            return StatusCode(500, new { message = "Failed to load fork source" });
        }
    }

    /// <summary>
    /// Fork a published marketplace app into the current user's account.
    /// </summary>
    [HttpPost("{id}/fork")]
    public async Task<ActionResult<AppModel>> Fork(Guid id, [FromBody] ForkAppRequest? request = null)
    {
        try
        {
            var newApp = await appsService.ForkAsync(id, request);
            return CreatedAtAction(nameof(Get), new { id = newApp.Id }, newApp);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error forking app {AppId}", id);
            return StatusCode(500, new { message = "Failed to fork app" });
        }
    }

    /// <summary>
    /// Export an app as a portable zip bundle.
    /// Includes app files and a manifest with transferable metadata.
    /// </summary>
    [HttpGet("{id}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var archive = await appArchiveService.ExportAsync(id, cancellationToken);
            if (archive is null)
            {
                return NotFound(new { message = "App not found" });
            }

            return File(archive.Content, "application/zip", archive.FileName);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error exporting app {AppId}", id);
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
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "No file uploaded or file is empty" });
            }

            if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Only .zip archives are supported" });
            }

            await using var stream = file.OpenReadStream();
            var result = await appArchiveService.ImportAsync(stream, file.FileName, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = result.App.Id }, result.App);
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (AppTagValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error importing app archive {FileName}", file?.FileName);
            return StatusCode(500, new { message = "Failed to import app" });
        }
    }

    [HttpPost]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> Post(NewApp app)
    {
        Guid appId;
        try { appId = await appsService.Create(app); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        var newApp = appsService.Get(appId);

        return CreatedAtAction(nameof(Get), new { id = newApp.Id }, newApp);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(Guid id, AppModel app)
    {
        try { await appsService.Update(id, app); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await appsService.Remove(id);

        return NoContent();
    }

    [HttpPost("{id}/publish")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> Publish(Guid id, [FromBody] PublishAppRequest request)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            await appsService.Publish(
                id,
                request.PublicUrl,
                request.RequiresAuthentication,
                request.ExecutionRunnerId,
                request.Category,
                request.Tags,
                request.IsForkable,
                request.RuntimeVisibility,
                request.CatalogVisibility,
                request.MarketplacePreviewMode);
        }
        catch (InvalidOperationException ex)
        {
            await applicationEventService.WriteAsync(new ApplicationEventWriteRequest
            {
                AppId = id,
                Level = ApplicationEventLevels.Error,
                Category = ApplicationEventCategories.Publishing,
                EventType = ApplicationEventTypes.AppPublishFailed,
                Message = "Application publish failed.",
                ActorUserId = userContextService.GetUserId(),
                Metadata = new { Error = ex.Message, request.PublicUrl }
            });
            return BadRequest(new { message = ex.Message });
        }

        return NoContent();
    }

    [HttpPost("{id}/unpublish")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> Unpublish(Guid id)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            await appsService.Unpublish(id);
        }
        catch (InvalidOperationException ex)
        {
            await applicationEventService.WriteAsync(new ApplicationEventWriteRequest
            {
                AppId = id,
                Level = ApplicationEventLevels.Error,
                Category = ApplicationEventCategories.Publishing,
                EventType = ApplicationEventTypes.AppUnpublishFailed,
                Message = "Application unpublish failed.",
                ActorUserId = userContextService.GetUserId(),
                Metadata = new { Error = ex.Message }
            });
            return BadRequest(new { message = ex.Message });
        }

        return NoContent();
    }

    [HttpPost("{id}/runtime-launch")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> CreateRuntimeLaunch(Guid id)
    {
        var app = await appsService.GetAccessibleAppInfoByIdAsync(id);
        if (app == null)
        {
            return NotFound(new { message = "App not found" });
        }

        var userId = userContextService.GetUserId();
        var expiresAtUtc = runtimeCookieService.GetDefaultExpiry();
        var runtimeCookie = runtimeCookieService.CreateRuntimeCookie(id, userId, expiresAtUtc);
        Response.Cookies.Append(
            RuntimeCookieConstants.GetCookieName(id),
            runtimeCookie,
            runtimeCookieService.BuildRuntimeCookieOptions(id, Request, expiresAtUtc));

        return Ok(new { runtimeToken = string.Empty });
    }

    [HttpGet("{id}/launch-runtime")]
    public async Task<IActionResult> LaunchRuntime(Guid id)
    {
        var app = await appsService.GetAccessibleAppInfoByIdAsync(id);
        if (app == null)
        {
            return NotFound(new { message = "App not found" });
        }

        var userId = userContextService.GetUserId();
        var expiresAtUtc = runtimeCookieService.GetDefaultExpiry();
        var runtimeCookie = runtimeCookieService.CreateRuntimeCookie(id, userId, expiresAtUtc);
        Response.Cookies.Append(
            RuntimeCookieConstants.GetCookieName(id),
            runtimeCookie,
            runtimeCookieService.BuildRuntimeCookieOptions(id, Request, expiresAtUtc));

        return Redirect(BuildRestrictedRuntimeUrl(id));
    }

    [HttpPut("{id}/runner")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> UpdateRunner(Guid id, [FromBody] UpdateRunnerRequest request)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try { await appsService.UpdateRunner(id, request.ExecutionRunnerId); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        return NoContent();
    }

    [HttpPost("{id}/upload-background")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadBackgroundImage(Guid id, IFormFile file)
    {
        var result = await appImageManagementService.UploadAsync(id, file, User.Identity?.Name);
        return ToImageOperationResult(result, "Failed to upload image");
    }

    [HttpPost("{id}/screenshots")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadScreenshot(Guid id, IFormFile file)
    {
        var result = await appImageManagementService.UploadScreenshotAsync(id, file, User.Identity?.Name);
        return ToImageOperationResult(result, "Failed to upload screenshot");
    }

    [HttpDelete("{id}/screenshots/{screenshotId:guid}")]
    public async Task<IActionResult> DeleteScreenshot(Guid id, Guid screenshotId)
    {
        var result = await appImageManagementService.DeleteScreenshotAsync(id, screenshotId, User.Identity?.Name);
        return ToImageOperationResult(result, "Failed to delete screenshot");
    }

    private IActionResult ToImageOperationResult(AppImageOperationResult result, string failureMessage)
        => result.StatusCode switch
        {
            StatusCodes.Status200OK when result.Payload is not null => Ok(result.Payload),
            StatusCodes.Status200OK => Ok(new { imageUrl = result.ImageUrl }),
            StatusCodes.Status204NoContent => NoContent(),
            StatusCodes.Status404NotFound => NotFound(new { message = result.ErrorMessage ?? "App not found" }),
            StatusCodes.Status400BadRequest => BadRequest(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { message = result.ErrorMessage ?? failureMessage })
        };

    /// <summary>
    /// Upload a zip archive of deployed live files.
    /// Files are merged into the app live directory.
    /// Use <c>?clean=true</c> to delete all current live files first (full replace).
    /// For delta / incremental updates, include only changed files in the zip (clean=false).
    /// Supported via API key authentication — no browser session required.
    /// </summary>
    /// <remarks>
    /// curl example:
    ///   curl -X POST https://your-server/api/apps/{id}/upload \
    ///     -H "X-API-Key: ak_developer_xxx" \
    ///     -F "file=@myapp.zip"
    /// </remarks>
    [HttpPost("{id}/upload")]
    [Authorize(Policy = "AllowApiKey")]
    [RequestSizeLimit(52_428_800)] // 50 MB
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadZip(Guid id, IFormFile file, [FromQuery] bool clean = false)
    {
        try
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { success = false, message = "No file uploaded or file is empty" });

            if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Only .zip archives are supported" });

            var result = await runtimeArchiveUploadService.UploadAsync(id, file, clean);
            if (result is null)
                return NotFound(new { success = false, message = "App not found" });

            return Ok(new { success = true, result.Written, result.Skipped, result.Errors });
        }
        catch (InvalidDataException)
        {
            return BadRequest(new { success = false, message = "Invalid or corrupt zip archive" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload zip for app {AppId}", id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private string BuildRestrictedRuntimeUrl(Guid appId)
    {
        var configuredBaseUrl = publishedAppsConfig.PublicBaseUrl?.Trim().TrimEnd('/');
        var baseUri = Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configuredUri)
            ? configuredUri
            : new Uri($"{Request.Scheme}://{Request.Host}");
        var rootDomain = baseUri.Host.StartsWith("apps.", StringComparison.OrdinalIgnoreCase)
            ? baseUri.Host["apps.".Length..]
            : baseUri.Host;
        var builder = new UriBuilder(baseUri)
        {
            Host = $"app-{appId:N}.apps.{rootDomain}",
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.ToString();
    }

}

public static class AppFileSystemScopes
{
    public const string Source = "source";
}

public class PublishAppRequest
{
    public required string PublicUrl { get; set; }
    public bool RequiresAuthentication { get; set; } = false;
    public Guid? ExecutionRunnerId { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public bool IsForkable { get; set; } = false;
    public string RuntimeVisibility { get; set; } = AppRuntimeVisibilities.Public;
    public string CatalogVisibility { get; set; } = AppCatalogVisibilities.Listed;
    public string MarketplacePreviewMode { get; set; } = MarketplacePreviewModes.Launchable;
}

public class LinkMultipleStepsRequest
{
    public IEnumerable<Guid> StepIds { get; set; } = new List<Guid>();
}

public class CreateAppStepRequest
{
    public string Name { get; set; } = string.Empty;
}

public class UpdateRunnerRequest
{
    public Guid? ExecutionRunnerId { get; set; }
}

public class SetSampleStatusRequest
{
    public bool IsSample { get; set; }
    public bool IsFeatured { get; set; }
}
