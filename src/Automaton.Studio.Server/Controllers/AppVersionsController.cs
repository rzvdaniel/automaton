using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Endpoints for managing app version snapshots and history.
/// App versions capture immutable snapshots of app files and custom steps,
/// typically triggered when new AI chat sessions are created.
/// </summary>
[Authorize]
[ApiController]
[Route("api/apps/{appId}/versions")]
public class AppVersionsController : BaseController
{
    private readonly AppVersionService appVersionService;
    private readonly AppsService appsService;
    private readonly ILogger<AppVersionsController> logger;

    public AppVersionsController(
        AppVersionService appVersionService,
        AppsService appsService,
        ILogger<AppVersionsController> logger)
    {
        this.appVersionService = appVersionService;
        this.appsService = appsService;
        this.logger = logger;
    }

    /// <summary>
    /// Manually create a version snapshot for an app.
    /// Typically called on-demand; automatic snapshots are created when new AI chat sessions start.
    /// </summary>
    [HttpPost("snapshot")]
    public async Task<ActionResult<AppVersionDto>> CreateSnapshot(Guid appId, [FromBody] CreateVersionSnapshotRequest? request = null)
    {
        try
        {
            var userId = GetUserId();
            if (userId == Guid.Empty)
            {
                return Unauthorized(new { message = "User context not available" });
            }

            // Verify the app exists and user has access
            var app = appsService.Get(appId);
            if (app is null)
            {
                return NotFound(new { message = "App not found" });
            }

            // Determine trigger type
            var triggerStr = request?.Trigger ?? "ManualSave";
            if (!Enum.TryParse<AppVersionTrigger>(triggerStr, out var trigger))
            {
                trigger = AppVersionTrigger.ManualSave;
            }

            // Create the snapshot
            var version = await appVersionService.CreateSnapshotAsync(
                appId,
                userId,
                trigger,
                aiSessionId: null,
                aiModel: null
            );

            var dto = new AppVersionDto
            {
                Id = version.Id,
                AppId = version.AppId,
                VersionNumber = version.VersionNumber,
                Summary = version.Summary ?? "Snapshot created",
                Trigger = version.Trigger.ToString(),
                CreatedAt = version.CreatedAt,
                CreatedBy = version.CreatedBy.ToString(),
                AiSessionId = version.AiSessionId,
                AiModel = version.AiModel
            };

            logger.LogInformation("[AppVersionsController] Created snapshot for app {AppId}, version {Version}", appId, version.VersionNumber);
            return CreatedAtAction(nameof(GetVersionDetail), new { appId, versionNumber = version.VersionNumber }, dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppVersionsController] Error creating snapshot for app {AppId}", appId);
            return StatusCode(500, new { message = "Failed to create version snapshot" });
        }
    }

    /// <summary>
    /// Get paginated list of version snapshots for an app.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AppVersionListDto>> ListVersions(
        Guid appId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            // Verify the app exists
            var app = appsService.Get(appId);
            if (app is null)
            {
                return NotFound(new { message = "App not found" });
            }

            var result = await appVersionService.GetVersionsAsync(appId, pageNumber, pageSize);

            var dto = new AppVersionListDto
            {
                AppId = appId,
                TotalCount = result.TotalCount,
                PageNumber = pageNumber,
                PageSize = pageSize,
                Versions = result.Versions.Select(v => new AppVersionDto
                {
                    Id = v.Id,
                    AppId = v.AppId,
                    VersionNumber = v.VersionNumber,
                    Summary = v.Summary ?? string.Empty,
                    Trigger = v.Trigger.ToString(),
                    CreatedAt = v.CreatedAt,
                    CreatedBy = v.CreatedBy.ToString(),
                    AiSessionId = v.AiSessionId,
                    AiModel = v.AiModel
                }).ToList()
            };

            return Ok(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppVersionsController] Error listing versions for app {AppId}", appId);
            return StatusCode(500, new { message = "Failed to list versions" });
        }
    }

    /// <summary>
    /// Get detailed information about a specific version, including file and step changes.
    /// </summary>
    [HttpGet("{versionNumber}")]
    public async Task<ActionResult<AppVersionDetailDto>> GetVersionDetail(Guid appId, int versionNumber)
    {
        try
        {
            // Verify the app exists
            var app = appsService.Get(appId);
            if (app is null)
            {
                return NotFound(new { message = "App not found" });
            }

            var version = await appVersionService.GetVersionDetailAsync(appId, versionNumber);
            if (version is null)
            {
                return NotFound(new { message = "Version not found" });
            }

            var dto = new AppVersionDetailDto
            {
                Id = version.Id,
                AppId = version.AppId,
                VersionNumber = version.VersionNumber,
                Summary = version.Summary ?? string.Empty,
                Trigger = version.Trigger.ToString(),
                CreatedAt = version.CreatedAt,
                CreatedBy = version.CreatedBy.ToString(),
                AiSessionId = version.AiSessionId,
                AiModel = version.AiModel,
                SnapshotPath = version.SnapshotPath ?? string.Empty,
                FilesChangeSummary = version.FilesChangeSummary,
                StepChanges = version.Steps?.Select(s => new VersionStepChangeDto
                {
                    Id = s.Id,
                    CustomStepId = s.StepId,
                    StepName = s.StepName,
                    Action = s.Action.ToString(),
                    Language = s.Language,
                    CodeSnapshot = s.CodeSnapshot,
                    VariablesSnapshot = s.VariablesSnapshot
                }).ToList() ?? new()
            };

            return Ok(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppVersionsController] Error getting version detail for app {AppId} version {Version}", appId, versionNumber);
            return StatusCode(500, new { message = "Failed to retrieve version detail" });
        }
    }

    /// <summary>
    /// Restore an app to a previous version.
    /// This copies files from the version snapshot back to the current app folder.
    /// </summary>
    [HttpPost("{versionNumber}/restore")]
    public async Task<ActionResult<RestoreVersionResponse>> RestoreVersion(Guid appId, int versionNumber, [FromBody] RestoreVersionRequest? request = null)
    {
        try
        {
            var userId = GetUserId();
            if (userId == Guid.Empty)
            {
                return Unauthorized(new { message = "User context not available" });
            }

            // Verify the app exists
            var app = appsService.Get(appId);
            if (app is null)
            {
                return NotFound(new { message = "App not found" });
            }

            // Perform the restore
            await appVersionService.RestoreVersionAsync(appId, versionNumber, userId);

            var response = new RestoreVersionResponse
            {
                AppId = appId,
                RestoredFromVersion = versionNumber,
                RestoredAt = DateTime.UtcNow,
                Message = $"App restored to version {versionNumber}"
            };

            logger.LogInformation("[AppVersionsController] Restored app {AppId} to version {Version} by user {UserId}", appId, versionNumber, userId);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "[AppVersionsController] Invalid restore request for app {AppId} version {Version}", appId, versionNumber);
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AppVersionsController] Error restoring app {AppId} to version {Version}", appId, versionNumber);
            return StatusCode(500, new { message = "Failed to restore version" });
        }
    }
}
