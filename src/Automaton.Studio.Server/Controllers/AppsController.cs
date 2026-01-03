using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public AppsController(
        AppsService appsService,
        AppStepsService appStepsService,
        UserImpersonationServiceFactory<AppsService> appsServiceFactory,
        ILogger<AppsController> logger,
        IWebHostEnvironment webHostEnvironment,
        UserContextService userContextService,
        GitHubService? githubService = null)
    {
        this.appsService = appsService;
        this.appStepsService = appStepsService;
        this.appsServiceFactory = appsServiceFactory;
        this.logger = logger;
        this.webHostEnvironment = webHostEnvironment;
        this.githubService = githubService;
        this.userContextService = userContextService;
    }

    [HttpGet]
    public IEnumerable<Models.AppModel> Get([FromQuery] Guid? projectId = null)
    {
        return appsService.List(projectId);
    }

    [HttpGet("{id}")]
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
        await appsService.Publish(id, request.PublicUrl, request.RequiresAuthentication, request.ExecutionRunnerId);

        return NoContent();
    }

    [HttpPost("{id}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid id)
    {
        await appsService.Unpublish(id);

        return NoContent();
    }

    [HttpPost("{id}/upload-background")]
    public async Task<IActionResult> UploadBackgroundImage(Guid id, [FromForm] IFormFile file)
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
            var appThumbnailsDir = Path.Combine(webHostEnvironment.WebRootPath, "app-thumbnails", id.ToString());
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

            // Return the relative URL
            var imageUrl = $"/app-thumbnails/{id}/{fileName}";

            logger.LogInformation("[AppsController] Background image uploaded for app {AppId}: {FileName} by user {User}", 
                id, fileName, User.Identity?.Name);

            return Ok(new { imageUrl = imageUrl });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppsController] Error uploading background image for app {AppId}", id);
            return StatusCode(500, new { message = "Failed to upload image" });
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
    public bool RequiresAuthentication { get; set; } = true;
    public Guid? ExecutionRunnerId { get; set; }
}

public class LinkMultipleStepsRequest
{
    public IEnumerable<Guid> StepIds { get; set; } = new List<Guid>();
}

public class CreateAppStepRequest
{
    public string Name { get; set; } = string.Empty;
}
