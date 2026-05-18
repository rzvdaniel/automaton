using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;
using Automaton.Studio.Server.Enums;
using Serilog;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Studio.Server.Services;

public class AppsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly RecentActivityService recentActivityService;
    private readonly UserContextService userContextService;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly AppFileStorageService fileStorageService;
    private readonly Serilog.ILogger logger;

    // ✅ Helper property to get current userId (respects impersonation)
    private Guid UserId => userContextService.GetUserId();

    public AppsService(
        ApplicationDbContext dataContext,
        UserContextService userContextService,
        RecentActivityService recentActivityService,
        ResourceAuthorizationService authorizationService,
        AppFileStorageService fileStorageService
    )
    {
        this.dataContext = dataContext;
        this.userContextService = userContextService;
        this.recentActivityService = recentActivityService;
        this.authorizationService = authorizationService;
        this.fileStorageService = fileStorageService;
        this.logger = Serilog.Log.ForContext<AppsService>();
    }

    public IEnumerable<Models.AppModel> List(Guid? projectId = null)
    {
        // Use authorization service to get apps filtered by project context
        var entities = authorizationService.GetAppsQueryByContext(projectId)
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots);
        var apps = entities.Select(e => MapToModel(e, null));

        return apps;
    }

    public Models.AppModel Get(Guid id)
    {
        // Use authorization service query to check access (personal + project-based)
        var appEntity = authorizationService.GetAccessibleAppsQuery()
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .SingleOrDefault(x => x.Id == id);

        if (appEntity == null)
        {
            return null;
        }

        // Load app definition from file system
        var ownerId = appEntity.WebAppUsers.FirstOrDefault()?.UserId ?? UserId;
        var definition = fileStorageService.LoadAppAsync(ownerId, id).Result;
        
        // If not in file system, fall back to database (for migration)
        if (definition == null && !string.IsNullOrEmpty(appEntity.Definition))
        {
            definition = JsonSerializer.Deserialize<Models.AppDefinition>(appEntity.Definition) ?? new Models.AppDefinition();
            
            // Migrate to file system
            fileStorageService.SaveAppAsync(ownerId, id, definition).Wait();
            logger.Information("Migrated app {AppId} from database to file system", id);
        }

        var app = MapToModel(appEntity, definition);

        return app;
    }

    /// <summary>
    /// Get app by public URL slug. Validates published status and ownership.
    /// Returns app only if it's published and owned by the current user (respects impersonation).
    /// </summary>
    public Models.AppModel GetBySlug(string slug)
    {
        var appEntity = dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .FirstOrDefault(x =>
                x.PublicUrl != null &&
                x.PublicUrl.ToLower() == slug.ToLower() &&
                x.IsPublished &&
                x.WebAppUsers.Any(u => u.UserId == UserId));

        if (appEntity == null)
        {
            return null;
        }

        // Load app definition from file system
        var ownerId = appEntity.WebAppUsers.FirstOrDefault()?.UserId ?? UserId;
        var definition = fileStorageService.LoadAppAsync(ownerId, appEntity.Id).Result;
        
        // If not in file system, fall back to database (for migration)
        if (definition == null && !string.IsNullOrEmpty(appEntity.Definition))
        {
            definition = JsonSerializer.Deserialize<Models.AppDefinition>(appEntity.Definition) ?? new Models.AppDefinition();
            
            // Migrate to file system
            fileStorageService.SaveAppAsync(ownerId, appEntity.Id, definition).Wait();
            logger.Information("Migrated app {AppId} from database to file system", appEntity.Id);
        }

        return MapToModel(appEntity, definition);
    }

    /// <summary>
    /// Get published app metadata by slug for access validation.
    /// Returns app info without requiring ownership (for authentication checks).
    /// Used by both AppsController and WebAppFlowExecutionController.
    /// </summary>
    public async Task<Models.PublishedAppInfo> GetPublishedAppInfoAsync(string slug, CancellationToken cancellationToken = default)
    {
        var appEntity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .FirstOrDefaultAsync(x =>
                x.PublicUrl != null &&
                x.PublicUrl.ToLower() == slug.ToLower() &&
                x.IsPublished &&
                x.MarketplaceStatus == MarketplaceAppStatuses.Approved,
                cancellationToken);

        if (appEntity == null)
        {
            return null;
        }

        var ownerId = appEntity.WebAppUsers.FirstOrDefault()?.UserId;

        return new Models.PublishedAppInfo
        {
            Id = appEntity.Id,
            Name = appEntity.Name,
            PublicUrl = appEntity.PublicUrl,
            RequiresAuthentication = appEntity.RequiresAuthentication,
            OwnerId = ownerId,
            ExecutionRunnerId = appEntity.ExecutionRunnerId
        };
    }

    /// <summary>
    /// Get app metadata by ID for preview mode (doesn't require app to be published).
    /// Used for testing backend steps in the App Designer before publishing.
    /// </summary>
    public async Task<Models.PublishedAppInfo> GetAppInfoByIdAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        var appEntity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .FirstOrDefaultAsync(x => x.Id == appId, cancellationToken);

        if (appEntity == null)
        {
            return null;
        }

        var ownerId = appEntity.WebAppUsers.FirstOrDefault()?.UserId;

        return new Models.PublishedAppInfo
        {
            Id = appEntity.Id,
            Name = appEntity.Name,
            PublicUrl = appEntity.PublicUrl,
            RequiresAuthentication = appEntity.RequiresAuthentication,
            OwnerId = ownerId,
            ExecutionRunnerId = appEntity.ExecutionRunnerId
        };
    }

    public async Task<Guid> Create(Models.NewApp app)
    {
        // If an execution runner id was provided, ensure it belongs to the owner (current user)
        if (app.ExecutionRunnerId.HasValue)
        {
            var runnerExists = dataContext.Runners
                .Any(r => r.Id == app.ExecutionRunnerId.Value && r.RunnerUsers.Any(u => u.UserId == UserId));

            if (!runnerExists)
            {
                throw new InvalidOperationException("Execution runner not found or you do not have access to it.");
            }
        }

        var entity = new Entities.WebApp
        {
            Id = Guid.NewGuid(),
            Name = app.Name,
            Summary = string.Empty,
            Description = string.Empty,
            IsPublished = false,
            PublicUrl = null,
            PublishedAt = null,
            Version = 1,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            Definition = "{}", // Empty - content is now in file system
            ExecutionRunnerId = app.ExecutionRunnerId
        };

        dataContext.WebApps.Add(entity);

        var appUser = new Entities.WebAppUser
        {
            WebAppId = entity.Id,
            UserId = UserId
        };

        dataContext.WebAppUsers.Add(appUser);
        dataContext.SaveChanges();

        // Create default app metadata in file system (no index.html — AI will write it)
        var defaultDefinition = new Models.AppDefinition
        {
            HtmlContent = null,
            Version = 1
        };
        await fileStorageService.SaveAppAsync(UserId, entity.Id, defaultDefinition);

        // Write automaton.json so external AI tools (Copilot, Cursor, Claude) can discover the app ID.
        var automatonConfig = JsonSerializer.Serialize(
            new { appId = entity.Id, name = entity.Name },
            new JsonSerializerOptions { WriteIndented = true });
        await fileStorageService.WriteAppFileAsync(UserId, entity.Id, "automaton.json", automatonConfig);

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, app.Name, entity.Id);

        logger.Information("Created app {AppId} with file system storage for user {UserId}", entity.Id, UserId);

        return entity.Id;
    }

    public async Task Update(Guid id, Models.AppModel app)
    {
        // Check if user can update this app (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateAppAsync(id))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update app {id}");
        }

        var entity = dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .SingleOrDefault(x => x.Id == id);

        if (entity == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        // Get owner ID for file storage
        var ownerId = entity.WebAppUsers.FirstOrDefault()?.UserId ?? UserId;

        entity.Name = app.Name;
        entity.Summary = app.Summary;
        entity.Description = app.Description;
        entity.IsPublished = app.IsPublished;
        entity.PublicUrl = app.PublicUrl;
        entity.RequiresAuthentication = app.RequiresAuthentication;
        entity.ExecutionRunnerId = app.ExecutionRunnerId;
        entity.IsProfileFeatured = app.IsProfileFeatured;
        entity.Updated = DateTime.UtcNow;
        // Definition is now empty - content in file system
        entity.Definition = "{}";

        // Public profile display fields
        entity.ThumbnailUrl = app.ThumbnailUrl;
        entity.Category = app.Category;
        entity.Tags = app.Tags;

        dataContext.SaveChanges();

        // Save app content to file system
        await fileStorageService.SaveAppAsync(ownerId, id, app.Definition);

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUpdated, app.Name, id);
        
        logger.Information("Updated app {AppId} in file system for user {UserId}", id, ownerId);
    }

    public async Task Remove(Guid id)
    {
        // Check if user can delete this app (must be personal owner AND not part of any project)
        if (!await authorizationService.CanDeleteAppAsync(id))
        {
            // Check if it's because the app is part of a project
            var isPartOfProject = await dataContext.ProjectApps
                .AnyAsync(pa => pa.WebAppId == id);

            if (isPartOfProject)
            {
                throw new InvalidOperationException($"Cannot delete app {id} because it is part of one or more projects. Remove it from all projects first.");
            }

            throw new UnauthorizedAccessException($"You do not have permission to delete app {id}");
        }

        var app = dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .SingleOrDefault(x => x.Id == id);

        if (app == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        var appName = app.Name;
        var ownerId = app.WebAppUsers.FirstOrDefault()?.UserId ?? UserId;

        // Delete from file system
        await fileStorageService.DeleteAppAsync(ownerId, id);

        // Delete from database
        dataContext.WebApps.Remove(app);
        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppDeleted, appName, id);
        
        logger.Information("Deleted app {AppId} from file system and database for user {UserId}", id, ownerId);
    }

    public async Task Publish(
        Guid id,
        string publicUrl,
        bool requiresAuthentication = true,
        Guid? executionRunnerId = null,
        string? category = null,
        string? tags = null)
    {
        // Check if user can update this app (needed for publishing)
        if (!await authorizationService.CanUpdateAppAsync(id))
        {
            throw new UnauthorizedAccessException($"You do not have permission to publish app {id}");
        }

        var entity = dataContext.WebApps.SingleOrDefault(x => x.Id == id);

        if (entity == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        // Validate URL slug format
        if (!IsValidUrlSlug(publicUrl))
        {
            throw new InvalidOperationException("Invalid URL slug format. Use lowercase letters, numbers, and hyphens only.");
        }

        // Check if URL is already taken
        if (dataContext.WebApps.Any(x => x.PublicUrl == publicUrl && x.Id != id))
        {
            throw new InvalidOperationException("This URL is already taken");
        }

        // If an execution runner id was provided, ensure it belongs to the owner
        if (executionRunnerId.HasValue)
        {
            var runnerExists = dataContext.Runners
                .Any(r => r.Id == executionRunnerId.Value && r.RunnerUsers.Any(u => u.UserId == UserId));

            if (!runnerExists)
            {
                throw new InvalidOperationException("Execution runner not found or you do not have access to it.");
            }
        }

        entity.IsPublished = true;
        entity.PublicUrl = publicUrl;
        entity.PublishedAt = DateTime.UtcNow;
        entity.RequiresAuthentication = requiresAuthentication;
        entity.Category = category?.Trim();
        entity.Tags = tags?.Trim();
        if (executionRunnerId.HasValue)
            entity.ExecutionRunnerId = executionRunnerId.Value;
        entity.Version++;
        entity.Updated = DateTime.UtcNow;

        dataContext.SaveChanges();

        logger.Information("App {AppId} published with slug {Slug} (v{Version}, Auth: {RequiresAuth}, Runner: {Runner})",
            id, publicUrl, entity.Version, requiresAuthentication, executionRunnerId);

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppPublished, entity.Name, id);
    }

    public async Task Unpublish(Guid id)
    {
        // Check if user can update this app (needed for unpublishing)
        if (!await authorizationService.CanUpdateAppAsync(id))
        {
            throw new UnauthorizedAccessException($"You do not have permission to unpublish app {id}");
        }

        var entity = dataContext.WebApps.SingleOrDefault(x => x.Id == id);

        if (entity == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        entity.IsPublished = false;
        entity.PublishedAt = null;
        entity.Updated = DateTime.UtcNow;
        // Keep PublicUrl for re-publishing with same slug

        dataContext.SaveChanges();

        logger.Information("App {AppId} unpublished (v{Version})", id, entity.Version);

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUnpublished, entity.Name, id);
    }

    /// <summary>
    /// Update the execution runner for an app
    /// </summary>
    public async Task UpdateRunner(Guid id, Guid? executionRunnerId)
    {
        // Check if user can update this app
        if (!await authorizationService.CanUpdateAppAsync(id))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update app {id}");
        }

        var entity = dataContext.WebApps.SingleOrDefault(x => x.Id == id);

        if (entity == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        entity.ExecutionRunnerId = executionRunnerId;
        entity.Updated = DateTime.UtcNow;

        dataContext.SaveChanges();

        logger.Information("App {AppId} runner updated to {RunnerId}", id, executionRunnerId);
    }

    /// <summary>
    /// Generate a unique URL slug from display name
    /// </summary>
    public string GenerateUniqueSlug(string displayName)
    {
        // Convert to lowercase and replace spaces with hyphens
        var slug = displayName.ToLowerInvariant()
            .Replace(' ', '-')
    .Replace('_', '-');

        // Remove special characters (keep only alphanumeric and hyphens)
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", "");

        // Remove multiple consecutive hyphens
        slug = Regex.Replace(slug, @"\-+", "-");

        // Trim hyphens from start and end
        slug = slug.Trim('-');
        if (string.IsNullOrEmpty(slug))
        {
            slug = "app";
        }

        // Ensure uniqueness
        var originalSlug = slug;
        var counter = 1;
        while (dataContext.WebApps.Any(x => x.PublicUrl == slug))
        {
            slug = $"{originalSlug}-{counter}";
            counter++;
        }

        return slug;
    }

    private static bool IsValidUrlSlug(string slug)
    {
        // URL slug validation: lowercase letters, numbers, hyphens
        return !string.IsNullOrEmpty(slug) && Regex.IsMatch(slug, @"^[a-z0-9\-]+$");
    }

    private static Models.AppModel MapToModel(Entities.WebApp entity, Models.AppDefinition? definition = null)
    {
        if (entity == null) return null;

        return new Models.AppModel
        {
            Id = entity.Id,
            UserId = entity.WebAppUsers?.FirstOrDefault()?.UserId,
            Name = entity.Name,
            Summary = entity.Summary,
            Description = entity.Description,
            IsPublished = entity.IsPublished,
            PublicUrl = entity.PublicUrl,
            PublishedAt = entity.PublishedAt,
            Version = entity.Version,
            RequiresAuthentication = entity.RequiresAuthentication,
            Created = entity.Created,
            Updated = entity.Updated,
            ExecutionRunnerId = entity.ExecutionRunnerId,
            // Project context
            Scope = entity.Scope,
            ProjectId = entity.ProjectId,
            Definition = definition ?? JsonSerializer.Deserialize<Models.AppDefinition>(entity.Definition ?? "{}") ?? new Models.AppDefinition(),
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
                .ToList() ?? new List<Models.AppScreenshotModel>(),
            Category = entity.Category,
            Tags = entity.Tags,
            MarketplaceStatus = entity.MarketplaceStatus,
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
        var entity = await dataContext.WebApps.FindAsync(appId);
        if (entity != null)
        {
            entity.ViewCount++;
            entity.LastViewedAt = DateTime.UtcNow;
            await dataContext.SaveChangesAsync();
            logger.Debug("Incremented view count for app {AppId} to {ViewCount}", appId, entity.ViewCount);
        }
    }

    /// <summary>
    /// Duplicate an app owned by the current user, copying all files.
    /// </summary>
    public async Task<Models.AppModel> DuplicateAsync(Guid appId)
    {
        var source = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .FirstOrDefaultAsync(x => x.Id == appId && x.WebAppUsers.Any(u => u.UserId == UserId));

        if (source == null)
            throw new InvalidOperationException("App not found or you do not have access.");

        var newId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var entity = new Entities.WebApp
        {
            Id = newId,
            Name = $"{source.Name} (Copy)",
            Summary = source.Summary,
            Description = source.Description,
            Definition = "{}",
            IsPublished = false,
            PublicUrl = null,
            PublishedAt = null,
            Version = 1,
            ExecutionRunnerId = source.ExecutionRunnerId,
            RequiresAuthentication = source.RequiresAuthentication,
            Scope = Enums.AccessScope.Personal,
            ProjectId = null,
            IsSample = false,
            IsFeatured = false,
            IsProfileFeatured = false,
            ThumbnailUrl = source.ThumbnailUrl,
            Category = source.Category,
            Tags = source.Tags,
            Created = now,
            Updated = now,
        };

        dataContext.WebApps.Add(entity);
        dataContext.WebAppUsers.Add(new Entities.WebAppUser { WebAppId = newId, UserId = UserId });
        await dataContext.SaveChangesAsync();

        await fileStorageService.CopyAppFilesAsync(UserId, appId, UserId, newId);

        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, entity.Name, newId);
        logger.Information("Duplicated app {SrcAppId} → {NewAppId} for user {UserId}", appId, newId, UserId);

        var newEntity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .FirstAsync(x => x.Id == newId);

        return MapToModel(newEntity, null);
    }

    /// <summary>
    /// Get metadata for the guided fork screen.
    /// </summary>
    public async Task<Models.ForkAppSourceModel> GetForkSourceAsync(Guid sourceAppId)
    {
        var source = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .FirstOrDefaultAsync(x =>
                x.Id == sourceAppId &&
                x.IsPublished &&
                x.MarketplaceStatus == MarketplaceAppStatuses.Approved);

        if (source == null)
            throw new InvalidOperationException("Fork source app not found.");

        var sourceOwner = source.WebAppUsers.FirstOrDefault()?.UserId
            ?? throw new InvalidOperationException("Fork source app has no owner.");

        var creator = await dataContext.Users
            .Where(u => u.Id == sourceOwner)
            .Select(u => new
            {
                u.PublicUsername,
                DisplayName = !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : u.Name
            })
            .FirstOrDefaultAsync();

        var sourceFiles = await fileStorageService.GetAppFilesAsync(sourceOwner, sourceAppId);
        var hasBackendExecution = source.ExecutionRunnerId.HasValue ||
            sourceFiles.Any(file => file.StartsWith("backend/", StringComparison.OrdinalIgnoreCase));

        return new Models.ForkAppSourceModel
        {
            Id = source.Id,
            Name = source.Name,
            Summary = source.Summary,
            Description = source.Description,
            ThumbnailUrl = source.ThumbnailUrl,
            CreatorPublicUsername = creator?.PublicUsername,
            CreatorDisplayName = creator?.DisplayName ?? "Automaton creator",
            HasBackendExecution = hasBackendExecution
        };
    }

    /// <summary>
    /// Fork a published marketplace app into the current user's account.
    /// </summary>
    public async Task<Models.AppModel> ForkAsync(Guid sourceAppId, Models.ForkAppRequest? request = null)
    {
        request ??= new Models.ForkAppRequest();

        var source = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .FirstOrDefaultAsync(x =>
                x.Id == sourceAppId &&
                x.IsPublished &&
                x.MarketplaceStatus == MarketplaceAppStatuses.Approved);

        if (source == null)
            throw new InvalidOperationException("Fork source app not found.");

        var sourceOwner = source.WebAppUsers.FirstOrDefault()?.UserId
            ?? throw new InvalidOperationException("Fork source app has no owner.");

        var creator = await dataContext.Users
            .Where(u => u.Id == sourceOwner)
            .Select(u => new
            {
                u.Id,
                DisplayName = !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : u.Name
            })
            .FirstOrDefaultAsync();

        var newId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var appName = NormalizeForkName(request.Name, source.Name);
        var publicUrl = request.PublishImmediately ? GenerateUniqueSlug(appName) : null;

        var entity = new Entities.WebApp
        {
            Id = newId,
            Name = appName,
            Summary = source.Summary,
            Description = source.Description,
            Definition = "{}",
            IsPublished = request.PublishImmediately,
            PublicUrl = publicUrl,
            PublishedAt = request.PublishImmediately ? now : null,
            Version = 1,
            ExecutionRunnerId = null,
            RequiresAuthentication = false,
            Scope = Enums.AccessScope.Personal,
            ProjectId = null,
            IsSample = false,
            IsFeatured = false,
            IsProfileFeatured = false,
            ThumbnailUrl = source.ThumbnailUrl,
            Category = source.Category,
            Tags = source.Tags,
            ForkedFromAppId = source.Id,
            ForkedFromAuthorUserId = sourceOwner,
            ForkedFromAppName = source.Name,
            ForkedFromAuthorName = creator?.DisplayName ?? "Automaton creator",
            Created = now,
            Updated = now,
        };

        dataContext.WebApps.Add(entity);
        dataContext.WebAppUsers.Add(new Entities.WebAppUser { WebAppId = newId, UserId = UserId });

        foreach (var screenshot in source.WebAppScreenshots.OrderBy(s => s.SortOrder))
        {
            dataContext.WebAppScreenshots.Add(new Entities.WebAppScreenshot
            {
                Id = Guid.NewGuid(),
                WebAppId = newId,
                ImageUrl = screenshot.ImageUrl,
                Caption = screenshot.Caption,
                SortOrder = screenshot.SortOrder,
                CreatedAt = now
            });
        }

        await dataContext.SaveChangesAsync();

        await fileStorageService.CopyAppFilesAsync(sourceOwner, sourceAppId, UserId, newId);

        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, entity.Name, newId);
        logger.Information("Forked app {SourceAppId} → {NewAppId} for user {UserId}", sourceAppId, newId, UserId);

        var newEntity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .FirstAsync(x => x.Id == newId);

        return MapToModel(newEntity, null);
    }

    private static string NormalizeForkName(string? requestedName, string sourceName)
    {
        var name = requestedName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"{sourceName} Remix";
        }

        return name.Length > 200 ? name[..200] : name;
    }

    /// <summary>
    /// Get all sample apps (publicly accessible)
    /// </summary>
    public async Task<IEnumerable<Models.AppModel>> GetSampleAppsAsync()
    {
        var entities = await dataContext.WebApps
            .Where(x => x.IsSample && x.IsPublished)
            .OrderByDescending(x => x.IsFeatured)
            .ThenByDescending(x => x.ViewCount)
            .Take(10)
            .ToListAsync();

        return entities.Select(e => MapToModel(e, null));
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
        logger.Information("Set sample status for app {AppId}: IsSample={IsSample}, IsFeatured={IsFeatured}", appId, isSample, isFeatured);
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
        logger.Information("Updated thumbnail URL for app {AppId}: {ThumbnailUrl}", appId, thumbnailUrl);
    }

    public async Task<Models.AppScreenshotModel> AddScreenshotAsync(Guid appId, string imageUrl, string? caption = null)
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

        logger.Information("Added screenshot {ScreenshotId} for app {AppId}", screenshot.Id, appId);

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

        logger.Information("Deleted screenshot {ScreenshotId} for app {AppId}", screenshotId, appId);
        return imageUrl;
    }
}
