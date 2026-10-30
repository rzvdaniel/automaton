using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Creates pre-deployment source restore points for Automaton CLI deployments.
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
    /// Create a version snapshot before a deployment.
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

            var snapshotRequest = NormalizeSnapshotRequest(request);

            // Create the snapshot
            var result = await appVersionService.CreateSnapshotAsync(
                appId,
                userId,
                snapshotRequest.Trigger,
                aiSessionId: null,
                aiModel: null,
                summary: snapshotRequest.Summary,
                source: snapshotRequest.Source,
                reason: snapshotRequest.Reason
            );

            if (!result.SnapshotCreated)
            {
                return Ok(new CreateVersionSnapshotResponse
                {
                    SnapshotCreated = false,
                    Message = result.Message
                });
            }

            var dto = MapVersionDto(result.Version!);

            logger.LogDebug("[AppVersionsController] Created snapshot for app {AppId}, version {Version}", appId, result.Version!.VersionNumber);
            return StatusCode(StatusCodes.Status201Created, new CreateVersionSnapshotResponse
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

    private static AppVersionDto MapVersionDto(Entities.AppVersion version)
    {
        return new AppVersionDto
        {
            Id = version.Id,
            AppId = version.AppId,
            VersionNumber = version.VersionNumber,
            Summary = version.Summary ?? "Snapshot created",
            Trigger = version.Trigger.ToString(),
            Source = AppVersionService.GetEffectiveSource(version),
            Reason = AppVersionService.GetEffectiveReason(version),
            CreatedAt = version.CreatedAt,
            CreatedBy = version.CreatedByUser?.Email ?? version.CreatedBy.ToString(),
            AiSessionId = version.AiSessionId,
            AiModel = version.AiModel,
            SnapshotCreated = true
        };
    }

    private static (AppVersionTrigger Trigger, string? Summary, string? Source, string? Reason) NormalizeSnapshotRequest(
        CreateVersionSnapshotRequest? request)
    {
        var triggerValue = request?.Trigger ?? "ManualSave";
        var trigger = Enum.TryParse<AppVersionTrigger>(triggerValue, out var parsedTrigger)
            ? parsedTrigger
            : AppVersionTrigger.ManualSave;

        return (
            trigger,
            NormalizeOptionalText(request?.Summary),
            NormalizeOptionalText(request?.Source),
            NormalizeOptionalText(request?.Reason));
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

}
