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
    private readonly AppStepsService appStepsService;
    private readonly ILogger<AppsController> logger;
    private readonly IWebHostEnvironment webHostEnvironment;
    private readonly UserContextService userContextService;
    private readonly AppFileStorageService fileStorageService;
    private readonly AppVersionService appVersionService;
    private readonly AppRuntimeArchiveUploadService runtimeArchiveUploadService;
    private readonly AppArchiveService appArchiveService;
    private readonly SettingsService settingsService;
    private readonly ConfirmedEmailGuardService confirmedEmailGuardService;
    private readonly PrivateAppLaunchService privateAppLaunchService;
    private readonly RuntimeCookieService runtimeCookieService;
    private readonly PublishedAppsConfig publishedAppsConfig;
    private readonly IApplicationEventService applicationEventService;

    public AppsController(
        AppsService appsService,
        AppStepsService appStepsService,
        ILogger<AppsController> logger,
        IWebHostEnvironment webHostEnvironment,
        UserContextService userContextService,
        AppFileStorageService fileStorageService,
        AppVersionService appVersionService,
        AppRuntimeArchiveUploadService runtimeArchiveUploadService,
        AppArchiveService appArchiveService,
        SettingsService settingsService,
        ConfirmedEmailGuardService confirmedEmailGuardService,
        PrivateAppLaunchService privateAppLaunchService,
        RuntimeCookieService runtimeCookieService,
        IOptions<PublishedAppsConfig> publishedAppsConfig,
        IApplicationEventService applicationEventService)
    {
        this.appsService = appsService;
        this.appStepsService = appStepsService;
        this.logger = logger;
        this.webHostEnvironment = webHostEnvironment;
        this.userContextService = userContextService;
        this.fileStorageService = fileStorageService;
        this.appVersionService = appVersionService;
        this.runtimeArchiveUploadService = runtimeArchiveUploadService;
        this.appArchiveService = appArchiveService;
        this.settingsService = settingsService;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
        this.privateAppLaunchService = privateAppLaunchService;
        this.runtimeCookieService = runtimeCookieService;
        this.publishedAppsConfig = publishedAppsConfig.Value;
        this.applicationEventService = applicationEventService;
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
        try
        {
            // Verify the user owns this app
            var app = appsService.Get(id);
            if (app == null)
            {
                logger.LogWarning("[AppsController] Upload attempt by user {User} to app not owned: {AppId}", 
                    User.Identity?.Name ?? "Unknown", id);
                return NotFound(new { message = "App not found" });
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "No file provided" });
            }

            // Validate file size (2MB max)
            const long maxFileSize = 2 * 1024 * 1024;
            if (file.Length > maxFileSize)
            {
                logger.LogWarning("[AppsController] File size exceeds limit for app {AppId}: {Size} bytes", id, file.Length);
                return BadRequest(new { message = "File size exceeds 2MB limit" });
            }

            // Validate file type - check both MIME type and extension
            var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            
            if (!allowedMimeTypes.Contains(file.ContentType))
            {
                logger.LogWarning("[AppsController] Invalid MIME type for app {AppId}: {MimeType}", id, file.ContentType);
                return BadRequest(new { message = "Invalid file type. Only image files are allowed." });
            }

            // Validate file extension to prevent executable uploads
            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(fileExtension))
            {
                logger.LogWarning("[AppsController] Invalid file extension for app {AppId}: {Extension}", id, fileExtension);
                return BadRequest(new { message = "Invalid file extension. Only .jpg, .png, .gif, and .webp are allowed." });
            }

            // Prevent path traversal attacks by validating filename
            if (Path.GetFileName(file.FileName) != file.FileName || file.FileName.Contains(".."))
            {
                logger.LogWarning("[AppsController] Suspicious filename detected for app {AppId}: {FileName}", id, file.FileName);
                return BadRequest(new { message = "Invalid filename" });
            }

            // Delete all old images for this app (keep only the new one)
            var appThumbnailsDir = GetAppMediaDirectory("app-thumbnails", id);
            await DeleteAllAppImagesAsync(appThumbnailsDir);

            // Create directory if it doesn't exist
            Directory.CreateDirectory(appThumbnailsDir);

            // Generate unique filename using GUID only - don't use user-provided filename
            var fileName = $"{Guid.NewGuid()}{fileExtension}";
            var filePath = Path.Combine(appThumbnailsDir, fileName);

            // Security check: ensure the full path is within the allowed directory
            var fullPath = Path.GetFullPath(filePath);
            var fullDirPath = Path.GetFullPath(appThumbnailsDir);
            
            if (!fullPath.StartsWith(fullDirPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                logger.LogError("[AppsController] Path traversal attempt detected for app {AppId}", id);
                return BadRequest(new { message = "Invalid file path" });
            }

            // Validate file content by checking magic bytes for images
            using (var stream = file.OpenReadStream())
            {
                if (!IsValidImageFile(stream, fileExtension))
                {
                    logger.LogWarning("[AppsController] File content doesn't match image format for app {AppId}", id);
                    return BadRequest(new { message = "File content is not a valid image" });
                }
                
                // Reset stream position before saving
                stream.Seek(0, SeekOrigin.Begin);

                // Save file
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await stream.CopyToAsync(fileStream);
                }
            }

            // Save the thumbnail URL to database
            var imageUrl = $"/app-thumbnails/{id}/{fileName}";
            await appsService.UpdateThumbnailUrlAsync(id, imageUrl);

            logger.LogDebug("[AppsController] Thumbnail uploaded for app {AppId}: {FileName} by user {User}",
                id, fileName, User.Identity?.Name);

            return Ok(new { imageUrl = imageUrl });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error uploading background image for app {AppId}", id);
            return StatusCode(500, new { message = "Failed to upload image" });
        }
    }

    [HttpPost("{id}/screenshots")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadScreenshot(Guid id, IFormFile file)
    {
        try
        {
            var app = appsService.Get(id);
            if (app == null)
            {
                logger.LogWarning("[AppsController] Screenshot upload attempt by user {User} to app not owned: {AppId}",
                    User.Identity?.Name ?? "Unknown", id);
                return NotFound(new { message = "App not found" });
            }

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file provided" });

            if ((app.Screenshots?.Count ?? 0) >= 8)
            {
                return BadRequest(new { message = "You can upload up to 8 screenshots per app" });
            }

            var validationFailure = ValidateScreenshotUpload(id, file, out var fileExtension);
            if (validationFailure != null) return validationFailure;

            var storedScreenshot = await StoreScreenshotFileAsync(id, file, fileExtension);
            if (storedScreenshot.ErrorMessage != null)
                return BadRequest(new { message = storedScreenshot.ErrorMessage });

            var imageUrl = $"/app-screenshots/{id}/{storedScreenshot.FileName}";
            var screenshot = await appsService.AddScreenshotAsync(id, imageUrl);

            logger.LogDebug("[AppsController] Screenshot uploaded for app {AppId}: {FileName} by user {User}",
                id, storedScreenshot.FileName, User.Identity?.Name);

            return Ok(screenshot);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error uploading screenshot for app {AppId}", id);
            return StatusCode(500, new { message = "Failed to upload screenshot" });
        }
    }

    private async Task<(string? FileName, string? ErrorMessage)> StoreScreenshotFileAsync(
        Guid appId,
        IFormFile file,
        string fileExtension)
    {
        var screenshotsDir = GetAppMediaDirectory("app-screenshots", appId);
        Directory.CreateDirectory(screenshotsDir);
        var fileName = $"{Guid.NewGuid()}{fileExtension}";
        var filePath = Path.Combine(screenshotsDir, fileName);
        var fullPath = Path.GetFullPath(filePath);
        var fullDirPath = Path.GetFullPath(screenshotsDir);
        if (!fullPath.StartsWith(fullDirPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            logger.LogError("[AppsController] Screenshot path traversal attempt detected for app {AppId}", appId);
            return (null, "Invalid file path");
        }

        using var stream = file.OpenReadStream();
        if (!IsValidImageFile(stream, fileExtension))
        {
            logger.LogWarning("[AppsController] Screenshot content doesn't match image format for app {AppId}", appId);
            return (null, "File content is not a valid image");
        }

        stream.Seek(0, SeekOrigin.Begin);
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream);
        return (fileName, null);
    }

    private IActionResult? ValidateScreenshotUpload(Guid appId, IFormFile file, out string fileExtension)
    {
        fileExtension = string.Empty;
        const long maxFileSize = 2 * 1024 * 1024;
        if (file.Length > maxFileSize)
        {
            logger.LogWarning("[AppsController] Screenshot file size exceeds limit for app {AppId}: {Size} bytes", appId, file.Length);
            return BadRequest(new { message = "File size exceeds 2MB limit" });
        }

        var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedMimeTypes.Contains(file.ContentType))
        {
            logger.LogWarning("[AppsController] Invalid screenshot MIME type for app {AppId}: {MimeType}", appId, file.ContentType);
            return BadRequest(new { message = "Invalid file type. Only image files are allowed." });
        }

        fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        if (!allowedExtensions.Contains(fileExtension))
        {
            logger.LogWarning("[AppsController] Invalid screenshot extension for app {AppId}: {Extension}", appId, fileExtension);
            return BadRequest(new { message = "Invalid file extension. Only .jpg, .png, .gif, and .webp are allowed." });
        }

        if (Path.GetFileName(file.FileName) != file.FileName || file.FileName.Contains(".."))
        {
            logger.LogWarning("[AppsController] Suspicious screenshot filename detected for app {AppId}: {FileName}", appId, file.FileName);
            return BadRequest(new { message = "Invalid filename" });
        }

        return null;
    }

    [HttpDelete("{id}/screenshots/{screenshotId:guid}")]
    public async Task<IActionResult> DeleteScreenshot(Guid id, Guid screenshotId)
    {
        try
        {
            var app = appsService.Get(id);
            if (app == null)
            {
                logger.LogWarning("[AppsController] Screenshot delete attempt by user {User} to app not owned: {AppId}",
                    User.Identity?.Name ?? "Unknown", id);
                return NotFound(new { message = "App not found" });
            }

            var imageUrl = await appsService.DeleteScreenshotAsync(id, screenshotId);
            DeleteImageFromUrl(imageUrl, GetAppMediaDirectory("app-screenshots", id));

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error deleting screenshot {ScreenshotId} for app {AppId}", screenshotId, id);
            return StatusCode(500, new { message = "Failed to delete screenshot" });
        }
    }

    /// <summary>
    /// Validate image file by checking magic bytes (file signatures)
    /// </summary>
    private static bool IsValidImageFile(Stream stream, string extension)
    {
        const int maxBytesToRead = 12;
        var buffer = new byte[maxBytesToRead];

        var bytesRead = stream.Read(buffer, 0, maxBytesToRead);
        if (bytesRead < 4)
            return false;

        return MatchesImageSignature(extension, buffer, bytesRead);
    }

    private static bool MatchesImageSignature(string extension, byte[] buffer, int bytesRead)
        => extension.ToLowerInvariant() switch
        {
            ".png" => buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47,
            ".jpg" or ".jpeg" => buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF,
            ".gif" => buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46,
            ".webp" => HasWebpSignature(buffer, bytesRead),
            _ => false
        };

    private static bool HasWebpSignature(byte[] buffer, int bytesRead)
        => bytesRead >= 12
           && buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46
           && buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50;

    /// <summary>
    /// Clean up old images for an app to prevent disk space exhaustion.
    /// Keeps only the 5 most recent images.
    /// </summary>
    private async Task DeleteAllAppImagesAsync(string appThumbnailsDir)
    {
        try
        {
            if (!Directory.Exists(appThumbnailsDir))
                return;

            var directoryInfo = new DirectoryInfo(appThumbnailsDir);
            var files = directoryInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();

            foreach (var file in files)
            {
                try
                {
                    file.Delete();
                    logger.LogDebug("[AppsController] Deleted old image: {FilePath}", file.FullName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[AppsController] Failed to delete old image: {FilePath}", file.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error cleaning up old images in {Directory}", appThumbnailsDir);
            // Don't throw - cleanup failure shouldn't prevent upload
        }
    }

    private void DeleteImageFromUrl(string? imageUrl, string parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        try
        {
            var fileName = Path.GetFileName(imageUrl);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            var filePath = Path.Combine(parentDirectory, fileName);
            var fullPath = Path.GetFullPath(filePath);
            var fullDirectoryPath = Path.GetFullPath(parentDirectory);

            if (!fullPath.StartsWith(fullDirectoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                logger.LogWarning("[AppsController] Skipped deleting image outside allowed directory: {ImageUrl}", imageUrl);
                return;
            }

            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[AppsController] Failed to delete image for URL {ImageUrl}", imageUrl);
        }
    }

    private string GetAppMediaDirectory(string mediaFolder, Guid appId)
    {
        return Path.Combine(
            webHostEnvironment.ContentRootPath,
            "AppData",
            "media",
            mediaFolder,
            appId.ToString());
    }

    /// <summary>
    /// Get steps linked to an app
    /// </summary>
    [HttpGet("{id}/steps")]
    public async Task<ActionResult<IEnumerable<LinkedStepInfo>>> GetLinkedSteps(Guid id)
    {
        try
        {
            var steps = await appStepsService.GetLinkedStepsAsync(id);
            return Ok(steps);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error getting linked steps for app {AppId}", id);
            return StatusCode(500, new { message = "Failed to get linked steps" });
        }
    }

    /// <summary>
    /// Get available steps that can be linked
    /// </summary>
    [HttpGet("{id}/steps/available")]
    public async Task<ActionResult<IEnumerable<LinkedStepInfo>>> GetAvailableSteps(Guid id)
    {
        try
        {
            var steps = await appStepsService.GetAvailableStepsAsync(id);
            return Ok(steps);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error getting available steps for app {AppId}", id);
            return StatusCode(500, new { message = "Failed to get available steps" });
        }
    }

    /// <summary>
    /// Link a step to an app
    /// </summary>
    [HttpPost("{appId}/steps/{stepId}/link")]
    public async Task<IActionResult> LinkStep(Guid appId, Guid stepId)
    {
        try
        {
            await appStepsService.LinkStepAsync(appId, stepId);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error linking step {StepId} to app {AppId}", stepId, appId);
            return StatusCode(500, new { message = "Failed to link step" });
        }
    }

    /// <summary>
    /// Link multiple steps to an app
    /// </summary>
    [HttpPost("{appId}/steps/link-multiple")]
    public async Task<IActionResult> LinkMultipleSteps(Guid appId, [FromBody] LinkMultipleStepsRequest request)
    {
        try
        {
            if (request.StepIds == null || !request.StepIds.Any())
            {
                return BadRequest(new { message = "No step IDs provided" });
            }

            await appStepsService.LinkStepsAsync(appId, request.StepIds);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error linking multiple steps to app {AppId}", appId);
            return StatusCode(500, new { message = "Failed to link steps" });
        }
    }

    /// <summary>
    /// Create a new step and automatically link it to the app
    /// </summary>
    [HttpPost("{appId}/steps/create")]
    public async Task<ActionResult<LinkedStepInfo>> CreateStepForApp(Guid appId, [FromBody] CreateAppStepRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
            {
                return BadRequest(new { message = "Step name is required" });
            }

            var step = await appStepsService.CreateStepForAppAsync(appId, request.Name);
            return CreatedAtAction(nameof(GetLinkedSteps), new { id = appId }, step);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error creating step for app {AppId}", appId);
            return StatusCode(500, new { message = "Failed to create step" });
        }
    }

    /// <summary>
    /// Unlink a step from an app
    /// </summary>
    [HttpDelete("{appId}/steps/{stepId}")]
    public async Task<IActionResult> UnlinkStep(Guid appId, Guid stepId)
    {
        try
        {
            await appStepsService.UnlinkStepAsync(appId, stepId);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error unlinking step {StepId} from app {AppId}", stepId, appId);
            return StatusCode(500, new { message = "Failed to unlink step" });
        }
    }
    
    /// <summary>
    /// Get files for a specific filesystem scope supported by the CLI.
    /// </summary>
    [HttpGet("{id}/filesystem")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> GetAppFilesystem(Guid id, [FromQuery] string scope = "source")
    {
        try
        {
            var appInfo = await appsService.GetAccessibleAppInfoByIdAsync(id);
            if (appInfo == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            var normalizedScope = NormalizeFileSystemScope(scope);
            if (normalizedScope == null)
            {
                return BadRequest(new { success = false, message = "Invalid filesystem scope" });
            }

            var files = await fileStorageService.GetSourceFilesAsync(ownerId, id);

            return Ok(new
            {
                success = true,
                scope = normalizedScope,
                editable = true,
                files = files.ToList()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get filesystem for app {AppId}", id);
            return StatusCode(500, new { success = false, message = ex.Message });
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
            if (appInfo == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            var normalizedScope = NormalizeFileSystemScope(scope);
            if (normalizedScope == null)
            {
                return BadRequest(new { success = false, message = "Invalid filesystem scope" });
            }

            var content = await fileStorageService.ReadSourceFileAsync(ownerId, id, path);

            if (content == null)
            {
                return NotFound(new { success = false, message = "File not found" });
            }

            return Ok(new
            {
                success = true,
                scope = normalizedScope,
                editable = true,
                content
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read filesystem file {Path} for app {AppId}", path, id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private static string? NormalizeFileSystemScope(string? scope)
    {
        return scope?.Trim().ToLowerInvariant() switch
        {
            AppFileSystemScopes.Source => AppFileSystemScopes.Source,
            _ => null
        };
    }

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
