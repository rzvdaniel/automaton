using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

public class AdminAppsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly AppFileStorageService fileStorageService;
    private readonly AdminAuthorizationService adminAuthorizationService;
    private readonly RecentActivityService recentActivityService;
    private readonly PublishedAppStorageService publishedAppStorageService;

    public AdminAppsService(
        ApplicationDbContext dataContext,
        ResourceAuthorizationService authorizationService,
        AppFileStorageService fileStorageService,
        AdminAuthorizationService adminAuthorizationService,
        RecentActivityService recentActivityService,
        PublishedAppStorageService publishedAppStorageService)
    {
        this.dataContext = dataContext;
        this.authorizationService = authorizationService;
        this.fileStorageService = fileStorageService;
        this.adminAuthorizationService = adminAuthorizationService;
        this.recentActivityService = recentActivityService;
        this.publishedAppStorageService = publishedAppStorageService;
    }

    public IEnumerable<Models.AppModel> ListOwnedByUserForAdmin(Guid userId, Guid? projectId = null)
    {
        EnsureAdminAccess();

        var entities = authorizationService.GetAppsQueryByContext(projectId, userId)
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots);

        return entities
            .AsEnumerable()
            .Select(entity =>
            {
                var app = AppsService.MapToModel(entity, null);
                var ownerId = entity.WebAppUsers?.FirstOrDefault()?.UserId ?? userId;
                AppsService.ApplyStorageMetadata(app, fileStorageService.GetAppStorageMetadataAsync(ownerId, entity.Id).Result);
                return app;
            });
    }

    public async Task<Models.AdminAppDetails?> GetDetailsForAdminAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        EnsureAdminAccess();

        var record = await RequireOwnedAppForAdminAsync(appId, cancellationToken, throwWhenMissing: false);

        if (record is null)
        {
            return null;
        }

        var metadata = await fileStorageService.GetAppStorageMetadataAsync(record.OwnerUserId, record.App.Id);
        var rootEntries = await fileStorageService.GetAppRootEntriesAsync(record.OwnerUserId, record.App.Id);
        var sourceFileCount = rootEntries.Count(path => path.StartsWith("source/", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("/", StringComparison.Ordinal));
        var publishedFileCount = rootEntries.Count(path => path.StartsWith("live/", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("/", StringComparison.Ordinal));
        var backendFileCount = rootEntries.Count(path =>
            !path.EndsWith("/", StringComparison.Ordinal) &&
            (path.StartsWith("source/backend/", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("live/backend/", StringComparison.OrdinalIgnoreCase)));
        var totalSizeBytes = GetDirectorySize(fileStorageService.GetAppRootPath(record.OwnerUserId, record.App.Id));

        var app = AppsService.MapToModel(record.App, null);
        AppsService.ApplyStorageMetadata(app, metadata);

        return new Models.AdminAppDetails
        {
            App = app,
            OwnerUserId = record.OwnerUserId,
            OwnerName = record.OwnerName,
            OwnerEmail = record.OwnerEmail,
            OwnerPublicUsername = record.OwnerPublicUsername,
            ExecutionRunnerName = record.ExecutionRunnerName,
            ProjectName = record.ProjectName,
            SourceFileCount = sourceFileCount,
            PublishedFileCount = publishedFileCount,
            BackendFileCount = backendFileCount,
            TotalSizeBytes = totalSizeBytes,
        };
    }

    public async Task<IEnumerable<string>> GetRootEntriesForAdminAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        var record = await RequireOwnedAppForAdminAsync(appId, cancellationToken);
        return await fileStorageService.GetAppRootEntriesAsync(record.OwnerUserId, record.App.Id);
    }

    public async Task<string?> ReadRootFileForAdminAsync(Guid appId, string relativePath, CancellationToken cancellationToken = default)
    {
        var record = await RequireOwnedAppForAdminAsync(appId, cancellationToken);
        return await fileStorageService.ReadAppRootFileAsync(record.OwnerUserId, record.App.Id, relativePath);
    }

    public async Task UnpublishForAdminAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        var record = await RequireOwnedAppForAdminAsync(appId, cancellationToken);
        record.App.IsPublished = false;
        record.App.PublicUrl = null;
        record.App.PublishedAt = null;
        record.App.Updated = DateTime.UtcNow;
        await dataContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateModerationForAdminAsync(Guid appId, Models.AdminMarketplaceModerationRequest request, CancellationToken cancellationToken = default)
    {
        var record = await RequireOwnedAppForAdminAsync(appId, cancellationToken);
        var previousReviewStatus = record.App.ReviewStatus;
        var previousAccessStatus = record.App.AccessStatus;
        var previousReason = record.App.MarketplaceModerationReason;

        record.App.ReviewStatus = NormalizeReviewStatus(request.ReviewStatus);
        record.App.AccessStatus = NormalizeAccessStatus(request.AccessStatus);
        record.App.MarketplaceStatus = MapLegacyMarketplaceStatus(record.App.ReviewStatus, record.App.AccessStatus);
        record.App.MarketplaceModerationReason = string.IsNullOrWhiteSpace(request.MarketplaceModerationReason)
            ? null
            : request.MarketplaceModerationReason.Trim();
        record.App.MarketplaceModeratedAt = DateTime.UtcNow;
        record.App.MarketplaceModeratedByUserId = adminAuthorizationService.GetCurrentUserId();
        record.App.Updated = DateTime.UtcNow;

        await SyncPublishedProjectionAsync(record, cancellationToken);
        await dataContext.SaveChangesAsync(cancellationToken);

        await recentActivityService.LogAppActivityAsync(
            Automaton.Core.Enums.ActivityType.AppUpdated,
            record.App.Name,
            record.App.Id,
            cancellationToken: cancellationToken);

        var moderationDescription = BuildModerationAuditDescription(
            record.App.Name,
            previousReviewStatus,
            record.App.ReviewStatus,
            previousAccessStatus,
            record.App.AccessStatus,
            previousReason,
            record.App.MarketplaceModerationReason);

        await recentActivityService.LogActivityAsync(
            Automaton.Core.Enums.ActivityType.AppUpdated,
            record.App.Name,
            record.App.Id,
            moderationDescription,
            cancellationToken);
    }

    private void EnsureAdminAccess() => adminAuthorizationService.EnsureAdmin();

    private async Task<AdminOwnedAppRecord?> RequireOwnedAppForAdminAsync(Guid appId, CancellationToken cancellationToken, bool throwWhenMissing = true)
    {
        EnsureAdminAccess();
        var app = await dataContext.WebApps
            .Include(item => item.WebAppUsers)
            .Include(item => item.WebAppScreenshots)
            .FirstOrDefaultAsync(item => item.Id == appId, cancellationToken);

        if (app is null)
        {
            if (throwWhenMissing)
            {
                throw new InvalidOperationException("App not found.");
            }

            return null;
        }

        var ownerLink = app.WebAppUsers
            .OrderBy(link => link.UserId)
            .FirstOrDefault();

        if (ownerLink is null)
        {
            if (throwWhenMissing)
            {
                throw new InvalidOperationException("App owner not found.");
            }

            return null;
        }

        var owner = await dataContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == ownerLink.UserId, cancellationToken);

        if (owner is null)
        {
            if (throwWhenMissing)
            {
                throw new InvalidOperationException("App owner not found.");
            }

            return null;
        }

        string? executionRunnerName = null;
        if (app.ExecutionRunnerId.HasValue)
        {
            executionRunnerName = await dataContext.Runners
                .AsNoTracking()
                .Where(runner => runner.Id == app.ExecutionRunnerId.Value)
                .Select(runner => runner.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? projectName = null;
        if (app.ProjectId.HasValue)
        {
            projectName = await dataContext.Projects
                .AsNoTracking()
                .Where(project => project.Id == app.ProjectId.Value)
                .Select(project => project.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new AdminOwnedAppRecord
        {
            App = app,
            OwnerUserId = owner.Id,
            OwnerName = !string.IsNullOrWhiteSpace(owner.DisplayName) ? owner.DisplayName : owner.Name,
            OwnerEmail = owner.Email ?? string.Empty,
            OwnerPublicUsername = owner.PublicUsername,
            ExecutionRunnerName = executionRunnerName,
            ProjectName = projectName,
        };
    }

    private static string NormalizeReviewStatus(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            AppReviewStatuses.Flagged => AppReviewStatuses.Flagged,
            _ => AppReviewStatuses.Approved,
        };
    }

    private static string NormalizeAccessStatus(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            AppAccessStatuses.Hidden => AppAccessStatuses.Hidden,
            AppAccessStatuses.Disabled => AppAccessStatuses.Disabled,
            _ => AppAccessStatuses.Active,
        };
    }

    private static string MapLegacyMarketplaceStatus(string reviewStatus, string accessStatus)
    {
        if (accessStatus == AppAccessStatuses.Disabled)
        {
            return MarketplaceAppStatuses.Banned;
        }

        if (accessStatus == AppAccessStatuses.Hidden)
        {
            return MarketplaceAppStatuses.Hidden;
        }

        return MarketplaceAppStatuses.Approved;
    }

    private async Task SyncPublishedProjectionAsync(AdminOwnedAppRecord record, CancellationToken cancellationToken)
    {
        if (!record.App.IsPublished || string.IsNullOrWhiteSpace(record.App.PublicUrl))
        {
            return;
        }

        if (record.App.AccessStatus != AppAccessStatuses.Active)
        {
            await publishedAppStorageService.UnpublishAsync(record.App.PublicUrl);
            return;
        }

        await publishedAppStorageService.PublishAsync(
            record.OwnerUserId,
            record.App.Id,
            new PublishedAppPackageMetadata(
                record.App.Id,
                record.App.PublicUrl,
                record.App.Name,
                record.App.Summary,
                record.App.Description,
                record.App.Version),
            cancellationToken);
    }

    private static string BuildModerationAuditDescription(
        string appName,
        string previousReviewStatus,
        string newReviewStatus,
        string previousAccessStatus,
        string newAccessStatus,
        string? previousReason,
        string? newReason)
    {
        var changes = new List<string>();

        if (!string.Equals(previousReviewStatus, newReviewStatus, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"ReviewStatus {previousReviewStatus} -> {newReviewStatus}");
        }

        if (!string.Equals(previousAccessStatus, newAccessStatus, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"AccessStatus {previousAccessStatus} -> {newAccessStatus}");
        }

        if (!string.Equals(previousReason?.Trim(), newReason?.Trim(), StringComparison.Ordinal))
        {
            changes.Add($"Note: {(string.IsNullOrWhiteSpace(newReason) ? "cleared" : newReason.Trim())}");
        }

        if (changes.Count == 0)
        {
            changes.Add("Moderation fields saved without state changes");
        }

        return $"Admin updated moderation for '{appName}': {string.Join("; ", changes)}";
    }

    private static long GetDirectorySize(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return 0;
        }

        return Directory
            .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
    }

    private sealed class AdminOwnedAppRecord
    {
        public WebApp App { get; set; } = null!;
        public Guid OwnerUserId { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public string? OwnerPublicUsername { get; set; }
        public string? ExecutionRunnerName { get; set; }
        public string? ProjectName { get; set; }
    }
}
