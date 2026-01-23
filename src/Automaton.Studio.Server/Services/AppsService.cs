using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;
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
        var entities = authorizationService.GetAppsQueryByContext(projectId);
        var apps = entities.Select(e => MapToModel(e, null));

        return apps;
    }

    public Models.AppModel Get(Guid id)
    {
        // Use authorization service query to check access (personal + project-based)
        var appEntity = authorizationService.GetAccessibleAppsQuery()
            .Include(x => x.WebAppUsers)
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
                x.IsPublished,
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

        // Create default app definition in file system
        var defaultDefinition = new Models.AppDefinition
        {
            HtmlContent = GetDefaultHtmlContent(),
            Version = 1
        };
        await fileStorageService.SaveAppAsync(UserId, entity.Id, defaultDefinition);

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, app.Name, entity.Id);

        logger.Information("Created app {AppId} with file system storage for user {UserId}", entity.Id, UserId);

        return entity.Id;
    }

    private string GetDefaultHtmlContent()
    {
        return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>My App</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 0; padding: 20px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); min-height: 100vh; display: flex; justify-content: center; align-items: center; }
        .container { background: white; padding: 40px; border-radius: 10px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); max-width: 600px; width: 100%; }
        h1 { color: #333; margin-top: 0; }
        p { color: #666; line-height: 1.6; }
        .button { background: #667eea; color: white; border: none; padding: 12px 24px; border-radius: 5px; cursor: pointer; font-size: 16px; margin-top: 20px; }
        .button:hover { background: #5568d3; }
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Welcome to Your App!</h1>
        <p>This is your app's starting template. Your app is now stored in the file system with full history tracking.</p>
        <p>Enable GitHub integration in Settings to sync with your personal repository for backup and collaboration.</p>
        <button class=""button"" onclick=""alert('Hello!')"">Click Me</button>
    </div>
    <script>console.log('App loaded!');</script>
</body>
</html>";
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

    public async Task Publish(Guid id, string publicUrl, bool requiresAuthentication = true, Guid? executionRunnerId = null)
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
        entity.ExecutionRunnerId = executionRunnerId;
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
            Definition = definition ?? JsonSerializer.Deserialize<Models.AppDefinition>(entity.Definition ?? "{}") ?? new Models.AppDefinition(),
            // Analytics
            ViewCount = entity.ViewCount,
            LastViewedAt = entity.LastViewedAt,
            // Sample/Featured
            IsSample = entity.IsSample,
            IsFeatured = entity.IsFeatured,
            // Public profile display
            ThumbnailUrl = entity.ThumbnailUrl,
            Category = entity.Category,
            Tags = entity.Tags
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
}
