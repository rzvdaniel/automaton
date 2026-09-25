using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Manages sample, local-development, and image metadata for apps.</summary>
public sealed class AppMetadataService(
    ApplicationDbContext dataContext,
    UserContextService userContextService,
    AppFileStorageService fileStorageService,
    IAuditEventService auditEventService)
{
    private Guid UserId => userContextService.GetUserId();
    public async Task<IEnumerable<AppModel>> GetSampleAppsAsync()
    {
        var entities = await dataContext.WebApps
            .Where(x => x.IsSample && x.IsPublished)
            .OrderByDescending(x => x.IsFeatured)
            .ThenByDescending(x => x.ViewCount)
            .Take(10)
            .ToListAsync();

        return entities.Select(e => AppsService.MapToModel(e, null));
    }

    public Task SetLocalDevelopmentMetadataAsync(
        Guid appId,
        Guid ownerId,
        bool? sourceAvailable = null,
        string? runtimeSource = null,
        bool? runtimeAvailable = null)
    {
        return fileStorageService.UpdateAppStorageMetadataAsync(ownerId, appId, metadata =>
        {
            metadata.DevelopmentMode = AppDevelopmentModes.Local;
            if (sourceAvailable.HasValue)
            {
                metadata.SourceAvailable = sourceAvailable.Value;
                if (sourceAvailable.Value && metadata.SourceModifiedAtUtc == null)
                {
                    metadata.SourceModifiedAtUtc = DateTime.UtcNow;
                }
            }
            if (!string.IsNullOrWhiteSpace(runtimeSource))
            {
                metadata.RuntimeSource = runtimeSource;
            }
            if (runtimeAvailable.HasValue)
            {
                metadata.RuntimeAvailable = runtimeAvailable.Value;
                if (runtimeAvailable.Value && metadata.RuntimeDeployedAtUtc == null)
                {
                    metadata.RuntimeDeployedAtUtc = DateTime.UtcNow;
                }
            }
        });
    }

    /// <summary>
    /// Set sample status for an app (admin only)
    /// </summary>
    public async Task SetSampleStatusAsync(Guid appId, bool isSample, bool isFeatured = false)
    {
        var entity = await dataContext.WebApps.FindAsync(appId);
        if (entity == null)
        {
            throw new InvalidOperationException($"App {appId} not found");
        }

        entity.IsSample = isSample;
        entity.IsFeatured = isFeatured;
        entity.Updated = DateTime.UtcNow;

        await dataContext.SaveChangesAsync();
        await auditEventService.WriteAsync(
            action: "UpdatedSampleStatus",
            entityType: "App",
            entityId: appId,
            message: "App sample status was updated",
            metadata: new { AppId = appId, IsSample = isSample, IsFeatured = isFeatured, UserId });

    }

    /// <summary>
    /// Update the thumbnail URL for an app
    /// </summary>
    public async Task UpdateThumbnailUrlAsync(Guid appId, string? thumbnailUrl)
    {
        var entity = await dataContext.WebApps.FindAsync(appId);
        if (entity == null)
        {
            throw new InvalidOperationException($"App {appId} not found");
        }

        entity.ThumbnailUrl = thumbnailUrl;
        entity.Updated = DateTime.UtcNow;

        await dataContext.SaveChangesAsync();
        await auditEventService.WriteAsync(
            action: "UpdatedThumbnail",
            entityType: "App",
            entityId: appId,
            message: "App thumbnail was updated",
            metadata: new { AppId = appId, ThumbnailUrl = thumbnailUrl, UserId });

    }

    public async Task<AppScreenshotModel> AddScreenshotAsync(Guid appId, string imageUrl, string? caption = null)
    {
        var nextSortOrder = await dataContext.WebAppScreenshots
            .Where(s => s.WebAppId == appId)
            .Select(s => (int?)s.SortOrder)
            .MaxAsync() ?? -1;

        var screenshot = new Entities.WebAppScreenshot
        {
            Id = Guid.NewGuid(),
            WebAppId = appId,
            ImageUrl = imageUrl,
            Caption = caption?.Trim(),
            SortOrder = nextSortOrder + 1,
            CreatedAt = DateTime.UtcNow
        };

        dataContext.WebAppScreenshots.Add(screenshot);

        var entity = await dataContext.WebApps.FindAsync(appId);
        if (entity != null)
        {
            entity.Updated = DateTime.UtcNow;
        }

        await dataContext.SaveChangesAsync();

        await auditEventService.WriteAsync(
            action: "AddedScreenshot",
            entityType: "App",
            entityId: appId,
            message: "App screenshot was added",
            metadata: new { AppId = appId, ScreenshotId = screenshot.Id, Caption = caption, UserId });


        return new Models.AppScreenshotModel
        {
            Id = screenshot.Id,
            ImageUrl = screenshot.ImageUrl,
            Caption = screenshot.Caption,
            SortOrder = screenshot.SortOrder
        };
    }

    public async Task<string?> DeleteScreenshotAsync(Guid appId, Guid screenshotId)
    {
        var screenshot = await dataContext.WebAppScreenshots
            .FirstOrDefaultAsync(s => s.WebAppId == appId && s.Id == screenshotId);

        if (screenshot == null)
        {
            throw new InvalidOperationException($"Screenshot {screenshotId} not found for app {appId}");
        }

        var imageUrl = screenshot.ImageUrl;
        dataContext.WebAppScreenshots.Remove(screenshot);

        var entity = await dataContext.WebApps.FindAsync(appId);
        if (entity != null)
        {
            entity.Updated = DateTime.UtcNow;
        }

        await dataContext.SaveChangesAsync();

        await auditEventService.WriteAsync(
            action: "DeletedScreenshot",
            entityType: "App",
            entityId: appId,
            message: "App screenshot was deleted",
            metadata: new { AppId = appId, ScreenshotId = screenshotId, UserId });

        return imageUrl;
    }

}
