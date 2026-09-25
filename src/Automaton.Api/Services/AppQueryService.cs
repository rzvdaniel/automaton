using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Queries apps through the current user's authorization and storage boundary.</summary>
public sealed class AppQueryService(
    ApplicationDbContext dataContext,
    UserContextService userContextService,
    ResourceAuthorizationService authorizationService,
    AppFileStorageService fileStorageService)
{
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<AppQueryService>();
    private Guid UserId => userContextService.GetUserId();
    public IEnumerable<AppModel> ListForMcp(Guid? projectId = null)
    {
        return authorizationService.GetAppsQueryByContext(projectId)
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .Include(x => x.DeploymentTargets)
            .AsSplitQuery().ToList()
            .Select(e => AppsService.MapToModel(e, null, ResolveAppOwnerId(e) ?? UserId));
    }

    internal static IReadOnlyList<string> ParseTags(string? tags) => AppTagNormalizer.Normalize(tags);

    public async Task<IReadOnlyList<string>> ListTagSuggestionsAsync(
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var tagValues = await authorizationService.GetAppsQueryByContext(projectId)
            .AsNoTracking()
            .Select(app => app.Tags)
            .ToListAsync(cancellationToken);

        return tagValues
            .SelectMany(AppTagNormalizer.Normalize)
            .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(tag => tag, StringComparer.Ordinal).First())
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tag => tag, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<AppTagSettingsDto?> GetTagSettingsAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        var app = await authorizationService.GetAccessibleAppsQuery()
            .AsNoTracking()
            .Where(app => app.Id == appId)
            .Select(item => new { item.Tags })
            .SingleOrDefaultAsync(cancellationToken);

        return app is null
            ? null
            : new AppTagSettingsDto { Tags = AppTagNormalizer.Normalize(app.Tags) };
    }

    public async Task<bool> UpdateTagSettingsAsync(
        Guid appId,
        IEnumerable<string>? tags,
        CancellationToken cancellationToken = default)
    {
        if (!await authorizationService.CanUpdateAppAsync(appId, cancellationToken))
        {
            return false;
        }

        var app = await dataContext.WebApps.SingleOrDefaultAsync(item => item.Id == appId, cancellationToken);
        if (app is null)
        {
            return false;
        }

        app.Tags = AppTagNormalizer.Serialize(tags);
        app.Updated = DateTime.UtcNow;
        await dataContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public AppModel Get(Guid id)
    {
        // Use authorization service query to check access (personal + project-based)
        var appEntity = authorizationService.GetAccessibleAppsQuery()
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .Include(x => x.DeploymentTargets)
            .SingleOrDefault(x => x.Id == id);

        if (appEntity == null)
        {
            return null;
        }

        // Load app definition from file system
        var ownerId = ResolveAppOwnerId(appEntity) ?? UserId;
        var definition = fileStorageService.LoadAppAsync(ownerId, id).Result;

        // If not in file system, fall back to database (for migration)
        if (definition == null && !string.IsNullOrEmpty(appEntity.Definition))
        {
            definition = JsonSerializer.Deserialize<AppDefinition>(appEntity.Definition) ?? new Models.AppDefinition();
            
            // Migrate to file system
            fileStorageService.SaveAppAsync(ownerId, id, definition).Wait();
            logger.Debug("Migrated app {AppId} from database to file system", id);
        }

        var app = AppsService.MapToModel(appEntity, definition, ownerId);
        AppsService.ApplyStorageMetadata(app, fileStorageService.GetAppStorageMetadataAsync(ownerId, id).Result);

        return app;
    }

    /// <summary>
    /// Get published app metadata by slug for access validation.
    /// Returns app info without requiring ownership (for authentication checks).
    /// Used by published app runtime execution endpoints.
    /// </summary>
    public async Task<PublishedAppInfo> GetPublishedAppInfoAsync(string slug, CancellationToken cancellationToken = default)
    {
        var appEntity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.DeploymentTargets)
            .FirstOrDefaultAsync(x =>
                x.PublicUrl != null &&
                x.PublicUrl.ToLower() == slug.ToLower() &&
                x.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                x.AccessStatus == AppAccessStatuses.Active,
                cancellationToken);

        if (appEntity == null)
        {
            return null;
        }

        var ownerId = await ResolveAppOwnerIdAsync(appEntity, cancellationToken);

        return new Models.PublishedAppInfo
        {
            Id = appEntity.Id,
            Name = appEntity.Name,
            PublicUrl = appEntity.PublicUrl,
            RequiresAuthentication = appEntity.RequiresAuthentication,
            OwnerId = ownerId,
            ExecutionRunnerId = GetDefaultRunnerId(appEntity)
        };
    }

    /// <summary>
    /// Get app metadata by ID only when the current user can access the app.
    /// Use this on authenticated Studio and preview endpoints to avoid resolving
    /// another user's storage or execution context from a raw app ID.
    /// </summary>
    public async Task<PublishedAppInfo?> GetAccessibleAppInfoByIdAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        return await GetAccessibleAppInfoByIdAsync(appId, UserId, cancellationToken);
    }

    public async Task<PublishedAppInfo?> GetAccessibleAppInfoByIdAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        var appEntity = await authorizationService.GetAccessibleAppsQuery(userId)
            .Include(x => x.WebAppUsers)
            .Include(x => x.DeploymentTargets)
            .FirstOrDefaultAsync(x => x.Id == appId, cancellationToken);

        if (appEntity == null)
        {
            return null;
        }

        var ownerId = await ResolveAppOwnerIdAsync(appEntity, cancellationToken);

        return new Models.PublishedAppInfo
        {
            Id = appEntity.Id,
            Name = appEntity.Name,
            PublicUrl = appEntity.PublicUrl,
            RequiresAuthentication = appEntity.RequiresAuthentication,
            OwnerId = ownerId,
            ExecutionRunnerId = GetDefaultRunnerId(appEntity)
        };
    }

    private async Task<Guid?> ResolveAppOwnerIdAsync(WebApp appEntity, CancellationToken cancellationToken)
    {
        if (appEntity.ProjectId.HasValue)
        {
            var projectOwnerId = await dataContext.Projects
                .Where(project => project.Id == appEntity.ProjectId.Value)
                .Select(project => (Guid?)project.OwnerId)
                .FirstOrDefaultAsync(cancellationToken);

            if (projectOwnerId.HasValue)
            {
                return projectOwnerId.Value;
            }
        }

        var linkedUserIds = await dataContext.WebAppUsers
            .Where(webAppUser => webAppUser.WebAppId == appEntity.Id)
            .Select(webAppUser => webAppUser.UserId)
            .Distinct()
            .OrderBy(userId => userId)
            .ToListAsync(cancellationToken);

        if (linkedUserIds.Count > 1)
        {
            logger.Warning(
                "App {AppId} has multiple linked users but no explicit owner field; using the lowest user id as a stable fallback",
                appEntity.Id);
        }

        return linkedUserIds.FirstOrDefault();
    }

    private Guid? ResolveAppOwnerId(WebApp appEntity)
    {
        if (appEntity.ProjectId.HasValue)
        {
            var projectOwnerId = dataContext.Projects
                .Where(project => project.Id == appEntity.ProjectId.Value)
                .Select(project => (Guid?)project.OwnerId)
                .FirstOrDefault();

            if (projectOwnerId.HasValue)
            {
                return projectOwnerId.Value;
            }
        }

        var linkedUserIds = dataContext.WebAppUsers
            .Where(webAppUser => webAppUser.WebAppId == appEntity.Id)
            .Select(webAppUser => webAppUser.UserId)
            .Distinct()
            .OrderBy(userId => userId)
            .ToList();

        if (linkedUserIds.Count > 1)
        {
            logger.Warning(
                "App {AppId} has multiple linked users but no explicit owner field; using the lowest user id as a stable fallback",
                appEntity.Id);
        }

        return linkedUserIds.FirstOrDefault();
    }

    private static Guid? GetDefaultRunnerId(WebApp app)
        => app.DeploymentTargets?.SingleOrDefault(target => target.IsDefault)?.ExecutionRunnerId;
}
