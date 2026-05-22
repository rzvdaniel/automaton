using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace Automaton.Studio.Server.Controllers;

public class AppsController : BaseController
{
    private readonly AppsService appsService;
    private readonly AppStepsService appStepsService;
    private readonly UserImpersonationServiceFactory<AppsService> appsServiceFactory;
    private readonly ILogger<AppsController> logger;
    private readonly IWebHostEnvironment webHostEnvironment;
    private readonly GitHubService? githubService;
    private readonly UserContextService userContextService;
    private readonly AppFileStorageService fileStorageService;
    private readonly AppVersionService appVersionService;
    private readonly AppArchiveService appArchiveService;
    private readonly SettingsService settingsService;

    public AppsController(
        AppsService appsService,
        AppStepsService appStepsService,
        UserImpersonationServiceFactory<AppsService> appsServiceFactory,
        ILogger<AppsController> logger,
        IWebHostEnvironment webHostEnvironment,
        UserContextService userContextService,
        AppFileStorageService fileStorageService,
        AppVersionService appVersionService,
        AppArchiveService appArchiveService,
        SettingsService settingsService,
        GitHubService? githubService = null)
    {
        this.appsService = appsService;
        this.appStepsService = appStepsService;
        this.appsServiceFactory = appsServiceFactory;
        this.logger = logger;
        this.webHostEnvironment = webHostEnvironment;
        this.githubService = githubService;
        this.userContextService = userContextService;
        this.fileStorageService = fileStorageService;
        this.appVersionService = appVersionService;
        this.appArchiveService = appArchiveService;
        this.settingsService = settingsService;
    }

    [HttpGet]
    public IEnumerable<Models.AppModel> Get([FromQuery] Guid? projectId = null)
    {
        return appsService.List(projectId);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "AllowApiKey")]
    public ActionResult<Models.AppModel> Get(Guid id)
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
    public async Task<ActionResult<IEnumerable<Models.AppModel>>> GetSampleApps()
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
    public async Task<ActionResult<IEnumerable<Models.AppModel>>> GetTemplates()
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
    public async Task<ActionResult<Models.AppModel>> Duplicate(Guid id)
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
    public async Task<ActionResult<Models.ForkAppSourceModel>> GetForkSource(Guid id)
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
    public async Task<ActionResult<Models.AppModel>> Fork(Guid id, [FromBody] Models.ForkAppRequest? request = null)
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
    public async Task<ActionResult<Models.AppModel>> Import(IFormFile file, CancellationToken cancellationToken)
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

    /// <summary>
    /// Get published app by public URL slug.
    /// Used by WebAppViewer to load apps accessed via /web/{slug} URLs.
    /// Validates authentication requirements and loads app as the owner.
    /// </summary>
    [HttpGet("slug/{slug}")]
    [AllowAnonymous] // We check authentication ourselves based on app settings
    public async Task<ActionResult<Models.AppModel>> GetBySlug(string slug)
    {
        try
        {
            logger.LogInformation("[AppsController] Loading app by slug: {Slug}", slug);

            // 1. Get published app metadata (without requiring ownership)
            var appInfo = await appsService.GetPublishedAppInfoAsync(slug);

            if (appInfo == null)
            {
                logger.LogWarning("[AppsController] App not found or not published: {Slug}", slug);

                return NotFound(new { message = "App not found or not published" });
            }

            // 2. Check authentication requirements
            var isUserAuthenticated = User.Identity?.IsAuthenticated ?? false;
            var viewerName = User.Identity?.Name ?? "Anonymous";

            if (appInfo.RequiresAuthentication && !isUserAuthenticated)
            {
                logger.LogWarning("[AppsController] Unauthenticated access to protected app: {AppName} (slug: {Slug})",
                    appInfo.Name, slug);

                return Unauthorized(new { message = "This app requires authentication" });
            }

            // 3. Validate app has owner
            if (appInfo.OwnerId == null)
            {
                logger.LogError("[AppsController] App has no owner: {AppName} (slug: {Slug})",
                    appInfo.Name, slug);

                return StatusCode(500, new { message = "App configuration error: no owner" });
            }

            // 4. Log access for traceability and security auditing
            logger.LogInformation(
                "[AppsController] App access: Slug={Slug}, AppId={AppId}, AppName={AppName}, " +
                "Owner={OwnerId}, Viewer={Viewer}, Authenticated={IsAuth}, RequiresAuth={RequiresAuth}",
                slug, appInfo.Id, appInfo.Name, appInfo.OwnerId, viewerName,
                isUserAuthenticated, appInfo.RequiresAuthentication);

            // 5. Load app as owner (with impersonation)
            var ownerAppsService = appsServiceFactory.CreateForUser(appInfo.OwnerId.Value);
            var app = ownerAppsService.Get(appInfo.Id);

            if (app == null)
            {
                logger.LogError("[AppsController] Failed to load app as owner: {AppId}", appInfo.Id);
                return NotFound(new { message = "App not found" });
            }

            logger.LogInformation("[AppsController] Successfully loaded app: {AppName} (ID: {AppId})", app.Name, app.Id);

            return Ok(app);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error loading app by slug: {Slug}", slug);
            return StatusCode(500, new { message = "An error occurred while loading the app" });
        }
    }

    [HttpPost]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> Post(Models.NewApp app)
    {
        var appId = await appsService.Create(app);

        var newApp = appsService.Get(appId);

        return CreatedAtAction(nameof(Get), new { id = newApp.Id }, newApp);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(Guid id, Models.AppModel app)
    {
        await appsService.Update(id, app);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await appsService.Remove(id);

        return NoContent();
    }

    [HttpPost("{id}/publish")]
    public async Task<IActionResult> Publish(Guid id, [FromBody] PublishAppRequest request)
    {
        await appsService.Publish(
            id,
            request.PublicUrl,
            request.RequiresAuthentication,
            request.ExecutionRunnerId,
            request.Category,
            request.Tags,
            request.IsForkable);

        return NoContent();
    }

    [HttpPost("{id}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid id)
    {
        await appsService.Unpublish(id);

        return NoContent();
    }

    [HttpPut("{id}/runner")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> UpdateRunner(Guid id, [FromBody] UpdateRunnerRequest request)
    {
        await appsService.UpdateRunner(id, request.ExecutionRunnerId);

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

            logger.LogInformation("[AppsController] Thumbnail uploaded for app {AppId}: {FileName} by user {User}",
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
            {
                return BadRequest(new { message = "No file provided" });
            }

            if ((app.Screenshots?.Count ?? 0) >= 8)
            {
                return BadRequest(new { message = "You can upload up to 8 screenshots per app" });
            }

            const long maxFileSize = 2 * 1024 * 1024;
            if (file.Length > maxFileSize)
            {
                logger.LogWarning("[AppsController] Screenshot file size exceeds limit for app {AppId}: {Size} bytes", id, file.Length);
                return BadRequest(new { message = "File size exceeds 2MB limit" });
            }

            var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

            if (!allowedMimeTypes.Contains(file.ContentType))
            {
                logger.LogWarning("[AppsController] Invalid screenshot MIME type for app {AppId}: {MimeType}", id, file.ContentType);
                return BadRequest(new { message = "Invalid file type. Only image files are allowed." });
            }

            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(fileExtension))
            {
                logger.LogWarning("[AppsController] Invalid screenshot extension for app {AppId}: {Extension}", id, fileExtension);
                return BadRequest(new { message = "Invalid file extension. Only .jpg, .png, .gif, and .webp are allowed." });
            }

            if (Path.GetFileName(file.FileName) != file.FileName || file.FileName.Contains(".."))
            {
                logger.LogWarning("[AppsController] Suspicious screenshot filename detected for app {AppId}: {FileName}", id, file.FileName);
                return BadRequest(new { message = "Invalid filename" });
            }

            var screenshotsDir = GetAppMediaDirectory("app-screenshots", id);
            Directory.CreateDirectory(screenshotsDir);

            var fileName = $"{Guid.NewGuid()}{fileExtension}";
            var filePath = Path.Combine(screenshotsDir, fileName);
            var fullPath = Path.GetFullPath(filePath);
            var fullDirPath = Path.GetFullPath(screenshotsDir);

            if (!fullPath.StartsWith(fullDirPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                logger.LogError("[AppsController] Screenshot path traversal attempt detected for app {AppId}", id);
                return BadRequest(new { message = "Invalid file path" });
            }

            using (var stream = file.OpenReadStream())
            {
                if (!IsValidImageFile(stream, fileExtension))
                {
                    logger.LogWarning("[AppsController] Screenshot content doesn't match image format for app {AppId}", id);
                    return BadRequest(new { message = "File content is not a valid image" });
                }

                stream.Seek(0, SeekOrigin.Begin);

                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);
            }

            var imageUrl = $"/app-screenshots/{id}/{fileName}";
            var screenshot = await appsService.AddScreenshotAsync(id, imageUrl);

            logger.LogInformation("[AppsController] Screenshot uploaded for app {AppId}: {FileName} by user {User}",
                id, fileName, User.Identity?.Name);

            return Ok(screenshot);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error uploading screenshot for app {AppId}", id);
            return StatusCode(500, new { message = "Failed to upload screenshot" });
        }
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
    private bool IsValidImageFile(Stream stream, string extension)
    {
        const int maxBytesToRead = 12;
        var buffer = new byte[maxBytesToRead];
        
        int bytesRead = stream.Read(buffer, 0, maxBytesToRead);
        if (bytesRead < 4)
            return false;

        return extension.ToLowerInvariant() switch
        {
            ".png" => buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47,
            ".jpg" or ".jpeg" => buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF,
            ".gif" => buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46,
            ".webp" => bytesRead >= 12 && 
                      buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 &&
                      buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50,
            _ => false
        };
    }

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
                    logger.LogInformation("[AppsController] Deleted old image: {FilePath}", file.FullName);
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
    public async Task<ActionResult<IEnumerable<Models.LinkedStepInfo>>> GetLinkedSteps(Guid id)
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
    public async Task<ActionResult<IEnumerable<Models.LinkedStepInfo>>> GetAvailableSteps(Guid id)
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
    public async Task<ActionResult<Models.LinkedStepInfo>> CreateStepForApp(Guid appId, [FromBody] CreateAppStepRequest request)
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
    /// Push app to GitHub repository
    /// </summary>
    [HttpPost("{id}/github/push")]
    public async Task<IActionResult> PushToGitHub(Guid id, [FromBody] GitHubPushRequest request)
    {
        try
        {
            if (githubService == null)
            {
                return StatusCode(500, new { success = false, message = "GitHub integration not available" });
            }

            var app = appsService.Get(id);
            if (app == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            var ownerId = userContextService.GetUserId();
            var commitSha = await githubService.PushToGitHubAsync(app, request.CommitMessage ?? "Update app from Automaton", ownerId);

            // Update app's GitHub settings
            if (app.Definition.GitHub != null)
            {
                app.Definition.GitHub.LastSyncedCommitSha = commitSha;
                app.Definition.GitHub.LastSyncedAt = DateTime.UtcNow;
                app.Definition.GitHub.SyncStatus = Models.GitHubSyncStatus.Synced;
                await appsService.Update(id, app);
            }

            return Ok(new
            {
                success = true,
                commitSha,
                message = "App pushed to GitHub successfully"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to push app {AppId} to GitHub", id);
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Pull app from GitHub repository
    /// </summary>
    [HttpPost("{id}/github/pull")]
    public async Task<IActionResult> PullFromGitHub(Guid id)
    {
        try
        {
            if (githubService == null)
            {
                return StatusCode(500, new { success = false, message = "GitHub integration not available" });
            }

            var app = appsService.Get(id);
            if (app == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            if (app.Definition.GitHub == null || string.IsNullOrEmpty(app.Definition.GitHub.RepositoryUrl))
            {
                return BadRequest(new { success = false, message = "App is not connected to a GitHub repository" });
            }

            var ownerId = userContextService.GetUserId();
            var pulledDefinition = await githubService.PullFromGitHubAsync(app.Definition.GitHub, ownerId, id);

            // Merge pulled definition into current app
            app.Definition.HtmlContent = pulledDefinition.HtmlContent;
            app.Definition.GitHub = pulledDefinition.GitHub;

            await appsService.Update(id, app);

            return Ok(new
            {
                success = true,
                message = "App pulled from GitHub successfully"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to pull app {AppId} from GitHub", id);
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Check GitHub sync status for an app
    /// </summary>
    [HttpGet("{id}/github/status")]
    public async Task<IActionResult> GetGitHubSyncStatus(Guid id)
    {
        try
        {
            if (githubService == null)
            {
                return StatusCode(500, new { success = false, message = "GitHub integration not available" });
            }

            var app = appsService.Get(id);
            if (app == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            if (app.Definition.GitHub == null || !app.Definition.GitHub.SyncEnabled)
            {
                return Ok(new
                {
                    syncEnabled = false,
                    status = "None"
                });
            }

            var status = await githubService.CheckSyncStatusAsync(app.Definition.GitHub);

            return Ok(new
            {
                syncEnabled = true,
                status = status.ToString(),
                repositoryUrl = app.Definition.GitHub.RepositoryUrl,
                branch = app.Definition.GitHub.Branch,
                lastSyncedAt = app.Definition.GitHub.LastSyncedAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check GitHub sync status for app {AppId}", id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Configure GitHub settings for an app
    /// </summary>
    [HttpPut("{id}/github/settings")]
    public async Task<IActionResult> UpdateGitHubSettings(Guid id, [FromBody] GitHubSettingsRequest request)
    {
        try
        {
            var app = appsService.Get(id);
            if (app == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            app.Definition.GitHub = new Models.GitHubSettings
            {
                RepositoryUrl = request.RepositoryUrl,
                Branch = request.Branch ?? "main",
                Path = request.Path,
                SyncEnabled = request.SyncEnabled
            };

            await appsService.Update(id, app);

            return Ok(new
            {
                success = true,
                message = "GitHub settings updated successfully"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update GitHub settings for app {AppId}", id);
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get list of editable source files for an app.
    /// </summary>
    [HttpGet("{id}/files")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> GetAppFiles(Guid id)
    {
        try
        {
            // Get the app owner ID from database (not current user)
            var appInfo = await appsService.GetAppInfoByIdAsync(id);
            if (appInfo == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            var files = await fileStorageService.GetSourceFilesAsync(ownerId, id);

            return Ok(new
            {
                success = true,
                files = files.ToList()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get files for app {AppId}", id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get files for a specific filesystem scope: source, live, or versions.
    /// </summary>
    [HttpGet("{id}/filesystem")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> GetAppFilesystem(Guid id, [FromQuery] string scope = "source")
    {
        try
        {
            var appInfo = await appsService.GetAppInfoByIdAsync(id);
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

            var files = normalizedScope switch
            {
                AppFileSystemScopes.Source => await fileStorageService.GetSourceFilesAsync(ownerId, id),
                AppFileSystemScopes.Live => await GetRuntimeFilesAsync(ownerId, id),
                AppFileSystemScopes.Versions => await fileStorageService.GetVersionFilesAsync(ownerId, id),
                _ => Enumerable.Empty<string>()
            };

            return Ok(new
            {
                success = true,
                scope = normalizedScope,
                editable = normalizedScope != AppFileSystemScopes.Versions,
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
    /// Get the real app-root filesystem view: source/, live/, versions/, and metadata files.
    /// </summary>
    [HttpGet("{id}/filesystem/root")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> GetAppRootFilesystem(Guid id)
    {
        try
        {
            var appInfo = await appsService.GetAppInfoByIdAsync(id);
            if (appInfo == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            var files = await fileStorageService.GetAppRootEntriesAsync(ownerId, id);

            return Ok(new
            {
                success = true,
                scope = "root",
                editable = false,
                files = files.ToList()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get root filesystem for app {AppId}", id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Read content of a root-level app file such as _metadata.json.
    /// Root files are visible in Studio but not editable.
    /// </summary>
    [HttpGet("{id}/filesystem/root/{*path}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> GetAppRootFilesystemFile(Guid id, string path)
    {
        try
        {
            var appInfo = await appsService.GetAppInfoByIdAsync(id);
            if (appInfo == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            var content = await fileStorageService.ReadAppRootFileAsync(ownerId, id, path);
            if (content == null)
            {
                return NotFound(new { success = false, message = "File not found" });
            }

            return Ok(new
            {
                success = true,
                scope = "root",
                editable = false,
                content
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read app root file {Path} for app {AppId}", path, id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Read content of a file from source, live, or versions scope.
    /// </summary>
    [HttpGet("{id}/filesystem/{scope}/{*path}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> GetAppFilesystemFile(Guid id, string scope, string path)
    {
        try
        {
            var appInfo = await appsService.GetAppInfoByIdAsync(id);
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

            var content = normalizedScope switch
            {
                AppFileSystemScopes.Source => await fileStorageService.ReadSourceFileAsync(ownerId, id, path),
                AppFileSystemScopes.Live => await ReadRuntimeFileAsync(ownerId, id, path),
                AppFileSystemScopes.Versions => await fileStorageService.ReadVersionFileAsync(ownerId, id, path),
                _ => null
            };

            if (content == null)
            {
                return NotFound(new { success = false, message = "File not found" });
            }

            return Ok(new
            {
                success = true,
                scope = normalizedScope,
                editable = normalizedScope != AppFileSystemScopes.Versions,
                content
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read filesystem file {Path} for app {AppId}", path, id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Write content to a file in source or live scope.
    /// Versions are immutable and cannot be modified.
    /// </summary>
    [HttpPut("{id}/filesystem/{scope}/{*path}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> WriteAppFilesystemFile(Guid id, string scope, string path, [FromBody] WriteFileRequest request)
    {
        try
        {
            var appInfo = await appsService.GetAppInfoByIdAsync(id);
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

            if (normalizedScope == AppFileSystemScopes.Versions)
            {
                return BadRequest(new { success = false, message = "Version snapshots are read-only" });
            }

            if (normalizedScope == AppFileSystemScopes.Source)
            {
                await fileStorageService.WriteSourceFileAsync(ownerId, id, path, request.Content);
            }
            else
            {
                await WriteRuntimeFileAsync(ownerId, id, path, request.Content);
            }

            return Ok(new { success = true, scope = normalizedScope, editable = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write filesystem file {Path} for app {AppId}", path, id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a file in source or live scope.
    /// Versions are immutable and cannot be modified.
    /// </summary>
    [HttpDelete("{id}/filesystem/{scope}/{*path}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> DeleteAppFilesystemFile(Guid id, string scope, string path)
    {
        try
        {
            var appInfo = await appsService.GetAppInfoByIdAsync(id);
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

            if (normalizedScope == AppFileSystemScopes.Versions)
            {
                return BadRequest(new { success = false, message = "Version snapshots are read-only" });
            }

            if (normalizedScope == AppFileSystemScopes.Source)
            {
                await fileStorageService.DeleteSourceFileAsync(ownerId, id, path);
            }
            else
            {
                await DeleteRuntimeFileAsync(ownerId, id, path);
            }

            return Ok(new { success = true, scope = normalizedScope, editable = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete filesystem file {Path} for app {AppId}", path, id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Read content of a specific editable source file.
    /// </summary>
    [HttpGet("{id}/files/{*path}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> GetAppFile(Guid id, string path)
    {
        try
        {
            // Get the app owner ID from database (not current user)
            var appInfo = await appsService.GetAppInfoByIdAsync(id);
            if (appInfo == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            var content = await fileStorageService.ReadSourceFileAsync(ownerId, id, path);

            if (content == null)
            {
                return NotFound(new { success = false, message = "File not found" });
            }

            return Ok(new
            {
                success = true,
                content = content,
                path = path
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized file access attempt for app {AppId}, path {Path}", id, path);
            return Unauthorized(new { success = false, message = "Access denied" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read file {Path} for app {AppId}", path, id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private async Task<IEnumerable<string>> GetRuntimeFilesAsync(Guid ownerId, Guid appId)
    {
        var frontendFiles = await fileStorageService.GetRuntimeFilesAsync(ownerId, appId);
        var backendFiles = await fileStorageService.GetRuntimeBackendFilesAsync(ownerId, appId);

        return frontendFiles.Select(path => $"frontend/{path.Replace('\\', '/')}")
            .Concat(backendFiles.Select(path => $"backend/{path.Replace('\\', '/')}"));
    }

    private async Task<string?> ReadRuntimeFileAsync(Guid ownerId, Guid appId, string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        if (normalizedPath.StartsWith("frontend/", StringComparison.OrdinalIgnoreCase))
        {
            return await fileStorageService.ReadRuntimeFileAsync(ownerId, appId, normalizedPath["frontend/".Length..]);
        }

        if (normalizedPath.StartsWith("backend/", StringComparison.OrdinalIgnoreCase))
        {
            return await fileStorageService.ReadRuntimeBackendFileAsync(ownerId, appId, normalizedPath["backend/".Length..]);
        }

        return null;
    }

    private async Task WriteRuntimeFileAsync(Guid ownerId, Guid appId, string path, string content)
    {
        var normalizedPath = path.Replace('\\', '/');
        if (normalizedPath.StartsWith("frontend/", StringComparison.OrdinalIgnoreCase))
        {
            await fileStorageService.WriteRuntimeFileAsync(ownerId, appId, normalizedPath["frontend/".Length..], content);
            return;
        }

        if (normalizedPath.StartsWith("backend/", StringComparison.OrdinalIgnoreCase))
        {
            await fileStorageService.WriteFileAtPathAsync(fileStorageService.GetAppRuntimeBackendDirectoryPath(ownerId, appId), normalizedPath["backend/".Length..], content, validateExtension: true);
            return;
        }

        throw new InvalidOperationException("Runtime files must live under frontend/ or backend/");
    }

    private async Task DeleteRuntimeFileAsync(Guid ownerId, Guid appId, string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        if (normalizedPath.StartsWith("frontend/", StringComparison.OrdinalIgnoreCase))
        {
            await fileStorageService.DeleteRuntimeFileAsync(ownerId, appId, normalizedPath["frontend/".Length..]);
            return;
        }

        if (normalizedPath.StartsWith("backend/", StringComparison.OrdinalIgnoreCase))
        {
            await fileStorageService.DeleteFileAtPathAsync(fileStorageService.GetAppRuntimeBackendDirectoryPath(ownerId, appId), normalizedPath["backend/".Length..]);
            return;
        }

        throw new InvalidOperationException("Runtime files must live under frontend/ or backend/");
    }

    private static string? NormalizeFileSystemScope(string? scope)
    {
        return scope?.Trim().ToLowerInvariant() switch
        {
            AppFileSystemScopes.Source => AppFileSystemScopes.Source,
            AppFileSystemScopes.Live => AppFileSystemScopes.Live,
            AppFileSystemScopes.Versions => AppFileSystemScopes.Versions,
            _ => null
        };
    }

    /// <summary>
    /// Write content to a specific editable source file.
    /// </summary>
    [HttpPut("{id}/files/{*path}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> PutAppFile(Guid id, string path, [FromBody] WriteFileRequest request)
    {
        try
        {
            // Get the app owner ID from database (not current user)
            var appInfo = await appsService.GetAppInfoByIdAsync(id);
            if (appInfo == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            await fileStorageService.WriteSourceFileAsync(ownerId, id, path, request.Content);

            // If this is index.html, also update the app definition
            if (path == "index.html")
            {
                var app = appsService.Get(id);
                if (app != null)
                {
                    app.Definition.HtmlContent = request.Content;
                    await appsService.Update(id, app);
                }
            }

            return Ok(new
            {
                success = true,
                message = "File saved successfully",
                path = path
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized file write attempt for app {AppId}, path {Path}", id, path);
            return Unauthorized(new { success = false, message = "Access denied" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write file {Path} for app {AppId}", path, id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a specific editable source file.
    /// </summary>
    [HttpDelete("{id}/files/{*path}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> DeleteAppFile(Guid id, string path)
    {
        try
        {
            // Don't allow deleting index.html or _metadata.json
            if (path == "index.html" || path == "_metadata.json")
            {
                return BadRequest(new { success = false, message = "Cannot delete system files" });
            }

            // Get the app owner ID from database (not current user)
            var appInfo = await appsService.GetAppInfoByIdAsync(id);
            if (appInfo == null)
            {
                return NotFound(new { success = false, message = "App not found" });
            }

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            await fileStorageService.DeleteSourceFileAsync(ownerId, id, path);

            return Ok(new
            {
                success = true,
                message = "File deleted successfully"
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized file delete attempt for app {AppId}, path {Path}", id, path);
            return Unauthorized(new { success = false, message = "Access denied" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete file {Path} for app {AppId}", path, id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
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
    ///     -H "X-API-Key: ak_execute_xxx" \
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
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "No file uploaded or file is empty" });

            if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Only .zip archives are supported" });

            // Resolve the app owner for file-system path construction
            var appInfo = await appsService.GetAppInfoByIdAsync(id);
            if (appInfo == null)
                return NotFound(new { success = false, message = "App not found" });

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            var runtimePath = fileStorageService.GetAppRuntimeFrontendDirectoryPath(ownerId, id);

            // clean=true: delete current public frontend live files before extracting.
            if (clean)
            {
                await fileStorageService.ClearFrontendRuntimeAsync(ownerId, id);
            }
            else
            {
                Directory.CreateDirectory(runtimePath);
            }

            var written = new List<string>();
            var skipped = new List<object>();
            var errors  = new List<object>();

            using var stream  = file.OpenReadStream();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                // Skip directory-only entries
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var relativePath = entry.FullName.Replace('\\', '/').TrimStart('/');

                // Skip platform metadata, hidden files, and source/dependency folders that should never be live output.
                if (ShouldSkipRuntimeUploadEntry(relativePath))
                {
                    skipped.Add(CreateSkippedEntry(relativePath, "Excluded by platform rule", "platform"));
                    continue;
                }

                if (!WebAppRuntimeFilePolicy.IsAllowedPublicPath(relativePath))
                {
                    skipped.Add(CreateSkippedEntry(relativePath, "Blocked by public runtime file policy", "platform"));
                    continue;
                }

                // Zip-slip protection: verify the resolved path stays inside live/frontend/.
                var destPath = Path.GetFullPath(Path.Combine(runtimePath, relativePath));
                var fullRuntimePath = Path.GetFullPath(runtimePath);
                if (!destPath.StartsWith(fullRuntimePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new { path = relativePath, reason = "Path traversal detected — file skipped." });
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    using var entryStream = entry.Open();
                    using var outStream   = System.IO.File.Create(destPath);
                    await entryStream.CopyToAsync(outStream);
                    written.Add(relativePath);
                }
                catch (Exception ex)
                {
                    errors.Add(new { path = relativePath, reason = ex.Message });
                }
            }

            logger.LogInformation(
                "Zip upload for app {AppId}: {Written} written, {Skipped} skipped, {Errors} errors (clean={Clean})",
                id, written.Count, skipped.Count, errors.Count, clean);

            await appsService.SetLocalDevelopmentMetadataAsync(
                id,
                ownerId,
                frontendRuntimeSource: Automaton.Studio.Server.Constants.AppRuntimeSources.CliBuild);

            if (written.Count > 0)
            {
                try
                {
                    await appVersionService.CreateSnapshotAsync(
                        id, userContextService.GetUserId(), AppVersionTrigger.ManualSave);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Version snapshot failed for app {AppId} after zip upload — upload still succeeded", id);
                }
            }

            return Ok(new { success = true, written, skipped, errors });
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

    [HttpPost("{id}/source/upload")]
    [Authorize(Policy = "AllowApiKey")]
    [RequestSizeLimit(52_428_800)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadSourceZip(Guid id, IFormFile file, [FromQuery] bool clean = true)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "No file uploaded or file is empty" });

            if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Only .zip archives are supported" });

            var appInfo = await appsService.GetAppInfoByIdAsync(id);
            if (appInfo == null)
                return NotFound(new { success = false, message = "App not found" });

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            var sourcePath = fileStorageService.GetAppSourceDirectoryPath(ownerId, id);

            if (clean)
            {
                await fileStorageService.ClearSourceAsync(ownerId, id);
            }
            else
            {
                Directory.CreateDirectory(sourcePath);
            }

            var written = new List<string>();
            var skipped = new List<object>();
            var errors = new List<object>();
            var deployRules = await settingsService.GetEffectiveAppDeployRulesAsync();

            using var stream = file.OpenReadStream();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var relativePath = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (ShouldSkipSourceUploadEntry(relativePath, deployRules.SourceExclude, out var sourceSkipReason))
                {
                    skipped.Add(CreateSkippedEntry(relativePath, sourceSkipReason, "platform"));
                    continue;
                }

                try
                {
                    AppFileStorageService.ValidateFileExtensionPublic(relativePath);
                }
                catch
                {
                    skipped.Add(CreateSkippedEntry(relativePath, "Blocked by allowed source file extension policy", "platform"));
                    continue;
                }

                var destPath = Path.GetFullPath(Path.Combine(sourcePath, relativePath));
                var fullSourcePath = Path.GetFullPath(sourcePath);
                if (!destPath.StartsWith(fullSourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new { path = relativePath, reason = "Path traversal detected — file skipped." });
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    using var entryStream = entry.Open();
                    using var outStream = System.IO.File.Create(destPath);
                    await entryStream.CopyToAsync(outStream);
                    written.Add(relativePath);
                }
                catch (Exception ex)
                {
                    errors.Add(new { path = relativePath, reason = ex.Message });
                }
            }

            logger.LogInformation(
                "Source zip upload for app {AppId}: {Written} written, {Skipped} skipped, {Errors} errors (clean={Clean})",
                id, written.Count, skipped.Count, errors.Count, clean);

            await appsService.SetLocalDevelopmentMetadataAsync(
                id,
                ownerId,
                frontendSourceAvailable: (await fileStorageService.GetAppStorageMetadataAsync(ownerId, id)).Frontend.SourceAvailable,
                frontendRuntimeSource: Automaton.Studio.Server.Constants.AppRuntimeSources.CliBuild,
                backendSourceAvailable: (await fileStorageService.GetAppStorageMetadataAsync(ownerId, id)).Backend.SourceAvailable);

            return Ok(new { success = true, written, skipped, errors });
        }
        catch (InvalidDataException)
        {
            return BadRequest(new { success = false, message = "Invalid or corrupt zip archive" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload source zip for app {AppId}", id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{id}/backend/upload")]
    [Authorize(Policy = "AllowApiKey")]
    [RequestSizeLimit(52_428_800)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadBackendZip(Guid id, IFormFile file, [FromQuery] bool clean = true)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "No file uploaded or file is empty" });

            if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "Only .zip archives are supported" });

            var appInfo = await appsService.GetAppInfoByIdAsync(id);
            if (appInfo == null)
                return NotFound(new { success = false, message = "App not found" });

            var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
            var runtimePath = fileStorageService.GetAppRuntimeBackendDirectoryPath(ownerId, id);

            if (clean)
            {
                await fileStorageService.ClearRuntimeBackendAsync(ownerId, id);
            }
            else
            {
                Directory.CreateDirectory(runtimePath);
            }

            var written = new List<string>();
            var skipped = new List<object>();
            var errors = new List<object>();
            var deployRules = await settingsService.GetEffectiveAppDeployRulesAsync();

            using var stream = file.OpenReadStream();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var relativePath = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (ShouldSkipBackendUploadEntry(relativePath, deployRules.BackendExclude, out var backendSkipReason, out var backendRuleSource))
                {
                    skipped.Add(CreateSkippedEntry(relativePath, backendSkipReason, backendRuleSource));
                    continue;
                }

                try
                {
                    AppFileStorageService.ValidateFileExtensionPublic(relativePath);
                }
                catch
                {
                    skipped.Add(CreateSkippedEntry(relativePath, "Blocked by allowed backend file extension policy", "platform"));
                    continue;
                }

                var destPath = Path.GetFullPath(Path.Combine(runtimePath, relativePath));
                var fullRuntimePath = Path.GetFullPath(runtimePath);
                if (!destPath.StartsWith(fullRuntimePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new { path = relativePath, reason = "Path traversal detected — file skipped." });
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    using var entryStream = entry.Open();
                    using var outStream = System.IO.File.Create(destPath);
                    await entryStream.CopyToAsync(outStream);
                    written.Add(relativePath);
                }
                catch (Exception ex)
                {
                    errors.Add(new { path = relativePath, reason = ex.Message });
                }
            }

            logger.LogInformation(
                "Backend zip upload for app {AppId}: {Written} written, {Skipped} skipped, {Errors} errors (clean={Clean})",
                id, written.Count, skipped.Count, errors.Count, clean);

            await appsService.SetLocalDevelopmentMetadataAsync(
                id,
                ownerId,
                backendRuntimeSource: Automaton.Studio.Server.Constants.AppRuntimeSources.CliDeploy);

            return Ok(new { success = true, written, skipped, errors });
        }
        catch (InvalidDataException)
        {
            return BadRequest(new { success = false, message = "Invalid or corrupt zip archive" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload backend zip for app {AppId}", id);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    private static bool ShouldSkipRuntimeUploadEntry(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fileName = segments.LastOrDefault() ?? string.Empty;
        if (fileName.StartsWith(".", StringComparison.Ordinal) ||
            fileName.Equals("_metadata.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("automaton.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("automaton.local.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("vite.config.", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return segments.Any(segment =>
            segment.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("src", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("source", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldSkipSourceUploadEntry(string relativePath, IEnumerable<string> excludeRules)
    {
        return ShouldSkipSourceUploadEntry(relativePath, excludeRules, out _);
    }

    private static bool ShouldSkipSourceUploadEntry(string relativePath, IEnumerable<string> excludeRules, out string reason)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            reason = "Empty path";
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fileName = segments.LastOrDefault() ?? string.Empty;
        if (fileName.StartsWith(".", StringComparison.Ordinal) ||
            fileName.Equals("_metadata.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("automaton.local.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Excluded by platform rule";
            return true;
        }

        if (segments.Any(segment =>
            segment.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".automaton", StringComparison.OrdinalIgnoreCase)))
        {
            reason = "Excluded by platform rule";
            return true;
        }

        if (AppFileStorageService.MatchesExcludeRule(relativePath, excludeRules))
        {
            reason = "Excluded by admin source deploy rule";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool ShouldSkipBackendUploadEntry(string relativePath, IEnumerable<string> excludeRules)
    {
        return ShouldSkipBackendUploadEntry(relativePath, excludeRules, out _, out _);
    }

    private static bool ShouldSkipBackendUploadEntry(string relativePath, IEnumerable<string> excludeRules, out string reason, out string ruleSource)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            reason = "Empty path";
            ruleSource = "platform";
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fileName = segments.LastOrDefault() ?? string.Empty;
        if (fileName.StartsWith(".", StringComparison.Ordinal) ||
            fileName.Equals("_metadata.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("automaton.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("automaton.local.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Excluded by platform rule";
            ruleSource = "platform";
            return true;
        }

        if (segments.Any(segment =>
            segment.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("venv", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".venv", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("__pycache__", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("frontend", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("mocks", StringComparison.OrdinalIgnoreCase)))
        {
            reason = "Excluded by platform rule";
            ruleSource = "platform";
            return true;
        }

        if (AppFileStorageService.MatchesExcludeRule(relativePath, excludeRules))
        {
            reason = "Excluded by admin backend deploy rule";
            ruleSource = "admin";
            return true;
        }

        reason = string.Empty;
        ruleSource = string.Empty;
        return false;
    }

    private static object CreateSkippedEntry(string path, string reason, string ruleSource)
    {
        return new
        {
            path,
            reason,
            ruleSource
        };
    }

}

public class WriteFileRequest
{
    public required string Content { get; set; }
}

public static class AppFileSystemScopes
{
    public const string Source = "source";
    public const string Live = "live";
    public const string Versions = "versions";
}

public class GitHubPushRequest
{
    public string? CommitMessage { get; set; }
}

public class GitHubSettingsRequest
{
    public string? RepositoryUrl { get; set; }
    public string? Branch { get; set; }
    public string? Path { get; set; }
    public bool SyncEnabled { get; set; }
}

public class PublishAppRequest
{
    public required string PublicUrl { get; set; }
    public bool RequiresAuthentication { get; set; } = false;
    public Guid? ExecutionRunnerId { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public bool IsForkable { get; set; } = false;
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
