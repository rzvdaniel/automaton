using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Automaton.Core.Models;
using System.Text.RegularExpressions;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Data;
using Automaton.Api.Config;

namespace Automaton.Api.Services;

public class AppsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly AppMetadataService appMetadataService;
    private readonly AppMutationService appMutationService;
    private readonly AppPublicationService appPublicationService;
    private readonly AppForkingService appForkingService;
    private readonly AppQueryService appQueryService;
    private readonly AppListingService appListingService;
    private readonly Serilog.ILogger logger;

    public AppsService(
        ApplicationDbContext dataContext,
        ResourceAuthorizationService authorizationService,
        AppMetadataService appMetadataService,
        AppMutationService appMutationService,
        AppPublicationService appPublicationService,
        AppForkingService appForkingService,
        AppQueryService appQueryService
    )
    {
        this.dataContext = dataContext;
        this.appMetadataService = appMetadataService;
        this.appMutationService = appMutationService;
        this.appPublicationService = appPublicationService;
        this.appForkingService = appForkingService;
        this.appQueryService = appQueryService;
        this.appListingService = new AppListingService(dataContext, authorizationService);
        this.logger = Serilog.Log.ForContext<AppsService>();
    }

    public async Task<IReadOnlyList<AppListItemDto>> ListAsync(Guid? projectId = null, CancellationToken cancellationToken = default)
        => await appListingService.ListAsync(projectId, cancellationToken);

    // Legacy projection retained for MCP callers until their contract is migrated.
    public IEnumerable<AppModel> ListForMcp(Guid? projectId = null) => appQueryService.ListForMcp(projectId);

    public async Task<IReadOnlyList<string>> ListTagSuggestionsAsync(
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
        => await appQueryService.ListTagSuggestionsAsync(projectId, cancellationToken);

    public Task<AppTagSettingsDto?> GetTagSettingsAsync(Guid appId, CancellationToken cancellationToken = default)
        => appQueryService.GetTagSettingsAsync(appId, cancellationToken);

    public Task<bool> UpdateTagSettingsAsync(
        Guid appId,
        IEnumerable<string>? tags,
        CancellationToken cancellationToken = default)
        => appQueryService.UpdateTagSettingsAsync(appId, tags, cancellationToken);

    public AppModel Get(Guid id) => appQueryService.Get(id);

    public Task<PublishedAppInfo> GetPublishedAppInfoAsync(string slug, CancellationToken cancellationToken = default)
        => appQueryService.GetPublishedAppInfoAsync(slug, cancellationToken);

    public Task<PublishedAppInfo?> GetAccessibleAppInfoByIdAsync(
        Guid appId,
        CancellationToken cancellationToken = default)
        => appQueryService.GetAccessibleAppInfoByIdAsync(appId, cancellationToken);

    public Task<PublishedAppInfo?> GetAccessibleAppInfoByIdAsync(
        Guid appId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => appQueryService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken);
    public Task<Guid> Create(NewApp app) => appMutationService.Create(app);

    public Task Update(Guid id, AppModel app) => appMutationService.Update(id, app);

    public Task Remove(Guid id, bool recoverInterrupted = false, bool abandonOfflineRuntimes = false)
        => appMutationService.Remove(id, recoverInterrupted, abandonOfflineRuntimes);
    public Task Publish(
        Guid id,
        string publicUrl,
        bool requiresAuthentication = true,
        Guid? executionRunnerId = null,
        string? category = null,
        string? tags = null,
        bool isForkable = false,
        string runtimeVisibility = AppRuntimeVisibilities.Public,
        string catalogVisibility = AppCatalogVisibilities.Listed,
        string marketplacePreviewMode = MarketplacePreviewModes.Launchable,
        CancellationToken cancellationToken = default)
        => appPublicationService.Publish(
            id, publicUrl, requiresAuthentication, executionRunnerId, category, tags, isForkable,
            runtimeVisibility, catalogVisibility, marketplacePreviewMode, cancellationToken);

    public Task Unpublish(Guid id, CancellationToken cancellationToken = default)
        => appPublicationService.Unpublish(id, cancellationToken);

    /// <summary>
    /// Update the execution runner for an app
    /// </summary>
    public Task UpdateRunner(Guid id, Guid? executionRunnerId)
        => appPublicationService.UpdateRunner(id, executionRunnerId);
    public string GenerateUniqueSlug(string displayName)
    {
        var originalSlug = NormalizeGeneratedSlug(displayName);
        var slug = originalSlug;

        // Ensure uniqueness
        var counter = 1;
        while (dataContext.WebApps.Any(x => x.PublicUrl == slug))
        {
            var suffix = $"-{counter}";
            var baseLength = Math.Max(1, PublishedAppHostNames.MaximumSlugLength - suffix.Length);
            var truncatedBase = originalSlug[..Math.Min(originalSlug.Length, baseLength)].TrimEnd('-');
            slug = $"{truncatedBase}{suffix}";
            counter++;
        }

        if (!PublishedAppHostNames.IsValidPublishedSlug(slug))
        {
            throw new InvalidOperationException(
                "Generated published slug violated the published host contract.");
        }

        return slug;
    }

    internal static string NormalizeGeneratedSlug(string displayName)
    {
        var slug = displayName.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-');
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", "", RegexOptions.None, TimeSpan.FromSeconds(1));
        slug = Regex.Replace(slug, @"\-+", "-", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim('-');
        if (string.IsNullOrEmpty(slug)) slug = "app";
        if (slug.Length > PublishedAppHostNames.MaximumSlugLength)
        {
            slug = slug[..PublishedAppHostNames.MaximumSlugLength].TrimEnd('-');
        }

        if (PublishedAppHostNames.IsPrivateAppLabel(slug))
        {
            slug = "app-generated";
        }

        return slug;
    }

    internal static AppModel MapToModel(WebApp entity, AppDefinition? definition = null, Guid? ownerId = null)
    {
        if (entity == null) return null;

        return new Models.AppModel
        {
            Id = entity.Id,
            UserId = ownerId ?? entity.WebAppUsers?.FirstOrDefault()?.UserId,
            Name = entity.Name,
            Summary = entity.Summary,
            Description = entity.Description,
            IsPublished = entity.IsPublished,
            IsForkable = entity.IsForkable,
            PublicUrl = entity.PublicUrl,
            PublishedAt = entity.PublishedAt,
            Version = entity.Version,
            RequiresAuthentication = entity.RequiresAuthentication,
            RuntimeVisibility = entity.RuntimeVisibility,
            CatalogVisibility = entity.CatalogVisibility,
            MarketplacePreviewMode = entity.MarketplacePreviewMode,
            Created = entity.Created,
            Updated = entity.Updated,
            ExecutionRunnerId = GetDefaultRunnerId(entity),
            // Project context
            Scope = entity.Scope,
            ProjectId = entity.ProjectId,
            Definition = definition ?? JsonSerializer.Deserialize<AppDefinition>(entity.Definition ?? "{}") ?? new Models.AppDefinition(),
            Application = ApplicationContractSerialization.Deserialize(entity.ApplicationContractJson),
            // Analytics
            ViewCount = entity.ViewCount,
            StarCount = entity.StarCount,
            LastViewedAt = entity.LastViewedAt,
            // Sample/Featured
            IsSample = entity.IsSample,
            IsFeatured = entity.IsFeatured,
            IsProfileFeatured = entity.IsProfileFeatured,
            // Public profile display
            ThumbnailUrl = entity.ThumbnailUrl,
            Screenshots = entity.WebAppScreenshots?
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.CreatedAt)
                .Select(s => new Models.AppScreenshotModel
                {
                    Id = s.Id,
                    ImageUrl = s.ImageUrl,
                    Caption = s.Caption,
                    SortOrder = s.SortOrder
                })
                .ToList() ?? new List<AppScreenshotModel>(),
            Category = entity.Category,
            Tags = entity.Tags,
            MarketplaceStatus = entity.MarketplaceStatus,
            ReviewStatus = entity.ReviewStatus,
            AccessStatus = entity.AccessStatus,
            MarketplaceModerationReason = entity.MarketplaceModerationReason,
            MarketplaceModeratedAt = entity.MarketplaceModeratedAt,
            MarketplaceModeratedByUserId = entity.MarketplaceModeratedByUserId,
            ForkedFromAppId = entity.ForkedFromAppId,
            ForkedFromAuthorUserId = entity.ForkedFromAuthorUserId,
            ForkedFromAppName = entity.ForkedFromAppName,
            ForkedFromAuthorName = entity.ForkedFromAuthorName
        };
    }

    /// <summary>
    /// Increment view count for an app (called when public app is accessed)
    /// </summary>
    public async Task IncrementViewCountAsync(Guid appId)
    {
        try
        {
            await dataContext.WebApps
                .Where(x => x.Id == appId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.ViewCount, x => x.ViewCount + 1)
                    .SetProperty(x => x.LastViewedAt, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to increment view count for app {AppId}", appId);
        }
    }

    /// <summary>
    /// Duplicate an app owned by the current user, copying editable source files.
    /// </summary>
    public Task<AppModel> DuplicateAsync(Guid appId) => appForkingService.DuplicateAsync(appId);

    public Task<ForkAppSourceModel> GetForkSourceAsync(Guid sourceAppId)
        => appForkingService.GetForkSourceAsync(sourceAppId);

    public Task<AppModel> ForkAsync(Guid sourceAppId, ForkAppRequest? request = null)
        => appForkingService.ForkAsync(sourceAppId, request);
    /// <summary>
    /// Get all sample apps (publicly accessible)
    /// </summary>
    public Task<IEnumerable<AppModel>> GetSampleAppsAsync() => appMetadataService.GetSampleAppsAsync();

    public Task SetLocalDevelopmentMetadataAsync(
        Guid appId,
        Guid ownerId,
        bool? sourceAvailable = null,
        string? runtimeSource = null,
        bool? runtimeAvailable = null)
        => appMetadataService.SetLocalDevelopmentMetadataAsync(
            appId, ownerId, sourceAvailable, runtimeSource, runtimeAvailable);

    public Task SetSampleStatusAsync(Guid appId, bool isSample, bool isFeatured = false)
        => appMetadataService.SetSampleStatusAsync(appId, isSample, isFeatured);

    public Task UpdateThumbnailUrlAsync(Guid appId, string? thumbnailUrl)
        => appMetadataService.UpdateThumbnailUrlAsync(appId, thumbnailUrl);

    public Task<AppScreenshotModel> AddScreenshotAsync(Guid appId, string imageUrl, string? caption = null)
        => appMetadataService.AddScreenshotAsync(appId, imageUrl, caption);

    public Task<string?> DeleteScreenshotAsync(Guid appId, Guid screenshotId)
        => appMetadataService.DeleteScreenshotAsync(appId, screenshotId);
    internal static void ApplyStorageMetadata(AppModel app, AppStorageMetadata? metadata)
    {
        if (app == null || metadata == null)
        {
            return;
        }

        app.DevelopmentMode = metadata.DevelopmentMode;
        app.SourceAvailable = metadata.SourceAvailable;
        app.RuntimeSource = metadata.RuntimeSource;
    }

    private static Guid? GetDefaultRunnerId(WebApp app)
    {
        return app.DeploymentTargets?
            .SingleOrDefault(target => target.IsDefault)?
            .ExecutionRunnerId;
    }
}
