using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Endpoints for managing app version snapshots and history.
/// App versions capture immutable snapshots of app files and custom steps,
/// typically triggered when new AI chat sessions are created.
/// </summary>
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
    /// Automatic snapshots are also created before each AI generation via AiAppConversationController.
    /// </summary>
    [HttpPost("snapshot")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<CreateVersionSnapshotResponse>> CreateSnapshot(Guid appId, [FromBody] CreateVersionSnapshotRequest? request = null)
    {
        try
        {
            // Verify the app exists and user has access
            var app = appsService.Get(appId);
            if (app is null)
            {
                return NotFound(new { message = "App not found" });
            }

            var userId = GetUserId();
            if (userId == Guid.Empty && app.UserId.HasValue)
            {
                userId = app.UserId.Value;
            }

            if (userId == Guid.Empty)
            {
                return Unauthorized(new { message = "User context not available" });
            }

            // Determine trigger type
            var triggerStr = request?.Trigger ?? "ManualSave";
            if (!Enum.TryParse<AppVersionTrigger>(triggerStr, out var trigger))
            {
                trigger = AppVersionTrigger.ManualSave;
            }

            // Create the snapshot
            var result = await appVersionService.CreateSnapshotAsync(
                appId,
                userId,
                trigger,
                aiSessionId: null,
                aiModel: null,
                summary: string.IsNullOrWhiteSpace(request?.Summary) ? null : request.Summary,
                source: string.IsNullOrWhiteSpace(request?.Source) ? null : request.Source,
                reason: string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason
            );

            if (!result.SnapshotCreated)
            {
                return Ok(new CreateVersionSnapshotResponse
                {
                    SnapshotCreated = false,
                    Message = result.Message
                });
            }

            var dto = MapVersionDto(result.Version!, appVersionService);

            logger.LogDebug("[AppVersionsController] Created snapshot for app {AppId}, version {Version}", appId, result.Version!.VersionNumber);
            return CreatedAtAction(nameof(GetVersionDetail), new { appId, versionNumber = result.Version.VersionNumber }, new CreateVersionSnapshotResponse
            {
                SnapshotCreated = true,
                Message = result.Message,
                Version = dto
            });
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
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        try
        {
            // Verify the app exists
            var app = appsService.Get(appId);
            if (app is null)
            {
                return NotFound(new { message = "App not found" });
            }

            var result = await appVersionService.GetVersionsAsync(appId, pageNumber, pageSize, search);

            var dto = new AppVersionListDto
            {
                AppId = appId,
                TotalCount = result.TotalCount,
                PageNumber = pageNumber,
                PageSize = pageSize,
                Versions = result.Versions.Select(v => MapVersionDto(v, appVersionService)).ToList()
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
                Source = appVersionService.GetEffectiveSource(version),
                Reason = appVersionService.GetEffectiveReason(version),
                CreatedAt = version.CreatedAt,
                CreatedBy = version.CreatedByUser?.Email ?? version.CreatedBy.ToString(),
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
                Message = $"App restored to version {versionNumber}"
            };

            logger.LogDebug("[AppVersionsController] Restored app {AppId} to version {Version} by user {UserId}", appId, versionNumber, userId);
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

    private static AppVersionDto MapVersionDto(Entities.AppVersion version, AppVersionService appVersionService)
    {
        return new AppVersionDto
        {
            Id = version.Id,
            AppId = version.AppId,
            VersionNumber = version.VersionNumber,
            Summary = version.Summary ?? "Snapshot created",
            Trigger = version.Trigger.ToString(),
            Source = appVersionService.GetEffectiveSource(version),
            Reason = appVersionService.GetEffectiveReason(version),
            CreatedAt = version.CreatedAt,
            CreatedBy = version.CreatedByUser?.Email ?? version.CreatedBy.ToString(),
            AiSessionId = version.AiSessionId,
            AiModel = version.AiModel,
            SnapshotCreated = true
        };
    }
}
