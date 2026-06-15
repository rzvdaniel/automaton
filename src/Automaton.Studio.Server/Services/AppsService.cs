using Automaton.Core.Enums;
using Automaton.Studio.Server.Constants;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Automaton.Studio.Server.Enums;
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
    private readonly PublishedAppStorageService publishedAppStorageService;
    private readonly IWebHostEnvironment webHostEnvironment;
    private readonly IAuditEventService auditEventService;
    private readonly Serilog.ILogger logger;

    // ✅ Helper property to get current userId (respects impersonation)
    private Guid UserId => userContextService.GetUserId();

    public AppsService(
        ApplicationDbContext dataContext,
        UserContextService userContextService,
        RecentActivityService recentActivityService,
        ResourceAuthorizationService authorizationService,
        AppFileStorageService fileStorageService,
        PublishedAppStorageService publishedAppStorageService,
        IWebHostEnvironment webHostEnvironment,
        IAuditEventService auditEventService
    )
    {
        this.dataContext = dataContext;
        this.userContextService = userContextService;
        this.recentActivityService = recentActivityService;
        this.authorizationService = authorizationService;
        this.fileStorageService = fileStorageService;
        this.publishedAppStorageService = publishedAppStorageService;
        this.webHostEnvironment = webHostEnvironment;
        this.auditEventService = auditEventService;
        this.logger = Serilog.Log.ForContext<AppsService>();
    }

    public IEnumerable<Models.AppModel> List(Guid? projectId = null)
    {
        // Use authorization service to get apps filtered by project context
        var entities = authorizationService.GetAppsQueryByContext(projectId)
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots);
        var apps = entities
            .AsEnumerable()
            .Select(e =>
            {
                var app = MapToModel(e, null);
                var ownerId = e.WebAppUsers?.FirstOrDefault()?.UserId ?? UserId;
                ApplyStorageMetadata(app, fileStorageService.GetAppStorageMetadataAsync(ownerId, e.Id).Result);
                app.HasDeployedFrontend = File.Exists(Path.Combine(
                    fileStorageService.GetAppRuntimeFrontendDirectoryPath(ownerId, e.Id),
                    "index.html"));
                return app;
            });

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
            logger.Debug("Migrated app {AppId} from database to file system", id);
        }

        var app = MapToModel(appEntity, definition);
        ApplyStorageMetadata(app, fileStorageService.GetAppStorageMetadataAsync(ownerId, id).Result);
        app.HasDeployedFrontend = File.Exists(Path.Combine(
            fileStorageService.GetAppRuntimeFrontendDirectoryPath(ownerId, id),
            "index.html"));

        return app;
    }

    /// <summary>
    /// Get published app metadata by slug for access validation.
    /// Returns app info without requiring ownership (for authentication checks).
    /// Used by published app runtime execution endpoints.
    /// </summary>
    public async Task<Models.PublishedAppInfo> GetPublishedAppInfoAsync(string slug, CancellationToken cancellationToken = default)
    {
        var appEntity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .FirstOrDefaultAsync(x =>
                x.PublicUrl != null &&
                x.PublicUrl.ToLower() == slug.ToLower() &&
                x.IsPublished &&
                x.AccessStatus == AppAccessStatuses.Active,
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
    /// Get app metadata by ID only when the current user can access the app.
    /// Use this on authenticated Studio and preview endpoints to avoid resolving
    /// another user's storage or execution context from a raw app ID.
    /// </summary>
    public async Task<Models.PublishedAppInfo?> GetAccessibleAppInfoByIdAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        return await GetAccessibleAppInfoByIdAsync(appId, UserId, cancellationToken);
    }

    public async Task<Models.PublishedAppInfo?> GetAccessibleAppInfoByIdAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        var appEntity = await authorizationService.GetAccessibleAppsQuery(userId)
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
        await fileStorageService.UpdateAppStorageMetadataAsync(UserId, entity.Id, metadata =>
        {
            metadata.DevelopmentMode = AppDevelopmentModes.Studio;
            metadata.Frontend.SourceAvailable = true;
            metadata.Frontend.RuntimeSource = AppRuntimeSources.Studio;
            metadata.Backend.SourceAvailable = true;
            metadata.Backend.RuntimeSource = AppRuntimeSources.Studio;
        });

        await fileStorageService.WriteSourceFileAsync(
            UserId,
            entity.Id,
            "frontend/index.html",
            await LoadDefaultFrontendTemplateAsync());

        // Write automaton.json so external AI tools (Copilot, Cursor, Claude) can discover the app ID.
        var automatonConfig = JsonSerializer.Serialize(
            new { appId = entity.Id, name = entity.Name },
            new JsonSerializerOptions { WriteIndented = true });
        await fileStorageService.WriteSourceFileAsync(UserId, entity.Id, "automaton.json", automatonConfig);

        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, app.Name, entity.Id);

        await auditEventService.WriteAsync(new Models.AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.App,
            EntityId = entity.Id,
            EventType = AuditEventTypes.AppCreated,
            Message = $"App '{entity.Name}' was created",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new
            {
                entity.Name,
                entity.ExecutionRunnerId
            }
        });


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
        var previousName = entity.Name;
        var previousSummary = entity.Summary;
        var previousDescription = entity.Description;
        var previousIsForkable = entity.IsForkable;
        var previousPublicUrl = entity.PublicUrl;
        var previousRequiresAuthentication = entity.RequiresAuthentication;
        var previousThumbnailUrl = entity.ThumbnailUrl;
        var previousCategory = entity.Category;
        var previousTags = entity.Tags;
        var previousIsProfileFeatured = entity.IsProfileFeatured;
        var previousExecutionRunnerId = entity.ExecutionRunnerId;

        entity.Name = app.Name;
        entity.Summary = app.Summary;
        entity.Description = app.Description;
        entity.IsPublished = app.IsPublished;
        entity.IsForkable = app.IsForkable;
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

        if (!string.Equals(previousName, entity.Name, StringComparison.Ordinal))
        {
            await auditEventService.WriteAsync(new Models.AuditEventWriteRequest
            {
                Action = AuditActions.Renamed,
                EntityType = AuditEntityTypes.App,
                EntityId = id,
                EventType = AuditEventTypes.AppRenamed,
                Message = $"App was renamed from '{previousName}' to '{entity.Name}'",
                Outcome = AuditOutcomes.Success,
                ActorUserId = UserId,
                Metadata = new
                {
                    PreviousName = previousName,
                    entity.Name,
                    OwnerUserId = ownerId
                }
            });
        }

        if (!string.Equals(previousPublicUrl, entity.PublicUrl, StringComparison.Ordinal))
        {
            await auditEventService.WriteAsync(new Models.AuditEventWriteRequest
            {
                Action = AuditActions.Changed,
                EntityType = AuditEntityTypes.App,
                EntityId = id,
                EventType = AuditEventTypes.AppSlugChanged,
                Message = $"App slug changed for '{entity.Name}'",
                Outcome = AuditOutcomes.Success,
                ActorUserId = UserId,
                Metadata = new
                {
                    PreviousSlug = previousPublicUrl,
                    NewSlug = entity.PublicUrl,
                    OwnerUserId = ownerId
                }
            });
        }

        var metadataChanged =
            !string.Equals(previousSummary, entity.Summary, StringComparison.Ordinal) ||
            !string.Equals(previousDescription, entity.Description, StringComparison.Ordinal) ||
            previousIsForkable != entity.IsForkable ||
            previousRequiresAuthentication != entity.RequiresAuthentication ||
            !string.Equals(previousThumbnailUrl, entity.ThumbnailUrl, StringComparison.Ordinal) ||
            !string.Equals(previousCategory, entity.Category, StringComparison.Ordinal) ||
            !string.Equals(previousTags, entity.Tags, StringComparison.Ordinal) ||
            previousIsProfileFeatured != entity.IsProfileFeatured ||
            previousExecutionRunnerId != entity.ExecutionRunnerId;

        if (metadataChanged)
        {
            await auditEventService.WriteAsync(new Models.AuditEventWriteRequest
            {
                Action = AuditActions.Updated,
                EntityType = AuditEntityTypes.App,
                EntityId = id,
                EventType = AuditEventTypes.AppMetadataUpdated,
                Message = $"App metadata was updated for '{entity.Name}'",
                Outcome = AuditOutcomes.Success,
                ActorUserId = UserId,
                Metadata = new
                {
                    entity.Name,
                    entity.PublicUrl,
                    entity.IsForkable,
                    entity.RequiresAuthentication,
                    entity.ThumbnailUrl,
                    entity.Category,
                    entity.Tags,
                    entity.IsProfileFeatured,
                    entity.ExecutionRunnerId,
                    OwnerUserId = ownerId
                }
            });
        }

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
        
        await auditEventService.WriteAsync(new Models.AuditEventWriteRequest
        {
            Action = AuditActions.Deleted,
            EntityType = AuditEntityTypes.App,
            EntityId = id,
            EventType = AuditEventTypes.AppDeleted,
            Message = $"App '{appName}' was deleted",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new
            {
                AppId = id,
                Name = appName,
                app.PublicUrl,
                app.ExecutionRunnerId,
                OwnerUserId = ownerId
            }
        });

    }

    public async Task Publish(
        Guid id,
        string publicUrl,
        bool requiresAuthentication = true,
        Guid? executionRunnerId = null,
        string? category = null,
        string? tags = null,
        bool isForkable = false)
    {
        // Check if user can update this app (needed for publishing)
        if (!await authorizationService.CanUpdateAppAsync(id))
        {
            throw new UnauthorizedAccessException($"You do not have permission to publish app {id}");
        }

        var entity = dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .SingleOrDefault(x => x.Id == id);

        if (entity == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        // Validate URL slug format
        if (!IsValidUrlSlug(publicUrl))
        {
            throw new InvalidOperationException("Invalid URL slug format. Use lowercase letters, numbers, and hyphens only.");
        }

        if (requiresAuthentication)
        {
            throw new InvalidOperationException("Restricted published apps are not supported by the public static app host yet. Use Studio preview for private and organization apps.");
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

        var ownerId = entity.WebAppUsers.FirstOrDefault()?.UserId ?? UserId;
        var nextVersion = entity.Version + 1;

        await publishedAppStorageService.PublishAsync(
            ownerId,
            id,
            new PublishedAppPackageMetadata(
                id,
                publicUrl,
                entity.Name,
                entity.Summary,
                entity.Description,
                nextVersion));

        entity.IsPublished = true;
        entity.PublicUrl = publicUrl;
        entity.PublishedAt = DateTime.UtcNow;
        entity.IsForkable = isForkable;
        entity.RequiresAuthentication = requiresAuthentication;
        entity.Category = category?.Trim();
        entity.Tags = tags?.Trim();
        if (executionRunnerId.HasValue)
            entity.ExecutionRunnerId = executionRunnerId.Value;
        entity.Version = nextVersion;
        entity.Updated = DateTime.UtcNow;

        dataContext.SaveChanges();

        await auditEventService.WriteAsync(new Models.AuditEventWriteRequest
        {
            Action = AuditActions.Published,
            EntityType = AuditEntityTypes.App,
            EntityId = id,
            EventType = AuditEventTypes.AppPublished,
            Message = $"App '{entity.Name}' was published",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new
            {
                Slug = publicUrl,
                entity.Version,
                RequiresAuthentication = requiresAuthentication,
                RunnerId = entity.ExecutionRunnerId,
                IsForkable = isForkable,
                OwnerUserId = ownerId
            }
        });


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

        await publishedAppStorageService.UnpublishAsync(entity.PublicUrl);

        entity.IsPublished = false;
        entity.PublishedAt = null;
        entity.Updated = DateTime.UtcNow;
        // Keep PublicUrl for re-publishing with same slug

        dataContext.SaveChanges();

        await auditEventService.WriteAsync(new Models.AuditEventWriteRequest
        {
            Action = AuditActions.Unpublished,
            EntityType = AuditEntityTypes.App,
            EntityId = id,
            EventType = AuditEventTypes.AppUnpublished,
            Message = $"App '{entity.Name}' was unpublished",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new
            {
                entity.PublicUrl,
                entity.Version
            }
        });


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

        var previousRunnerId = entity.ExecutionRunnerId;
        entity.ExecutionRunnerId = executionRunnerId;
        entity.Updated = DateTime.UtcNow;

        dataContext.SaveChanges();

        await auditEventService.WriteAsync(new Models.AuditEventWriteRequest
        {
            Action = executionRunnerId.HasValue ? AuditActions.Assigned : AuditActions.Unassigned,
            EntityType = AuditEntityTypes.App,
            EntityId = id,
            EventType = executionRunnerId.HasValue
                ? AuditEventTypes.RunnerAssignedToApp
                : AuditEventTypes.RunnerUnassignedFromApp,
            Message = executionRunnerId.HasValue
                ? "Runner was assigned to app execution"
                : "Runner was unassigned from app execution",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new
            {
                AppId = id,
                PreviousRunnerId = previousRunnerId,
                RunnerId = executionRunnerId
            }
        });

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

    internal static Models.AppModel MapToModel(Entities.WebApp entity, Models.AppDefinition? definition = null)
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
            IsForkable = entity.IsForkable,
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

        await fileStorageService.CopySourceFilesAsync(UserId, appId, UserId, newId);

        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, entity.Name, newId);
        await auditEventService.WriteAsync(
            action: "Duplicated",
            entityType: "App",
            entityId: newId,
            message: $"App '{entity.Name}' was duplicated",
            metadata: new { SourceAppId = appId, NewAppId = newId, UserId });


        var newEntity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .FirstAsync(x => x.Id == newId);

        var duplicated = MapToModel(newEntity, null);
        ApplyStorageMetadata(duplicated, await fileStorageService.GetAppStorageMetadataAsync(UserId, newId));
        return duplicated;
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
                x.ReviewStatus == AppReviewStatuses.Approved &&
                x.AccessStatus == AppAccessStatuses.Active);

        if (source == null)
            throw new InvalidOperationException("Fork source app not found.");

        if (!source.IsForkable)
            throw new InvalidOperationException("Forking is disabled for this app.");

        var sourceOwner = source.WebAppUsers.FirstOrDefault()?.UserId
            ?? throw new InvalidOperationException("Fork source app has no owner.");

        if (!fileStorageService.AppSourceExists(sourceOwner, sourceAppId))
            throw new InvalidOperationException("Fork source app has no source files.");

        var creator = await dataContext.Users
            .Where(u => u.Id == sourceOwner)
            .Select(u => new
            {
                u.PublicUsername,
                DisplayName = !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : u.Name
            })
            .FirstOrDefaultAsync();

        var hasBackendExecution = fileStorageService.HasBackendSourceEntries(sourceOwner, sourceAppId);

        return new Models.ForkAppSourceModel
        {
            Id = source.Id,
            Name = source.Name,
            PublicUrl = source.PublicUrl,
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
                x.ReviewStatus == AppReviewStatuses.Approved &&
                x.AccessStatus == AppAccessStatuses.Active);

        if (source == null)
            throw new InvalidOperationException("Fork source app not found.");

        if (!source.IsForkable)
            throw new InvalidOperationException("Forking is disabled for this app.");

        var sourceOwner = source.WebAppUsers.FirstOrDefault()?.UserId
            ?? throw new InvalidOperationException("Fork source app has no owner.");

        if (!fileStorageService.AppSourceExists(sourceOwner, sourceAppId))
            throw new InvalidOperationException("Fork source app has no source files.");

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
            IsForkable = false,
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

        await fileStorageService.CopySourceFilesAsync(sourceOwner, sourceAppId, UserId, newId);
        await fileStorageService.CopyRuntimeFrontendFilesAsync(sourceOwner, sourceAppId, UserId, newId);

        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, entity.Name, newId);
        await auditEventService.WriteAsync(new Models.AuditEventWriteRequest
        {
            Action = AuditActions.Forked,
            EntityType = AuditEntityTypes.App,
            EntityId = newId,
            EventType = AuditEventTypes.AppForked,
            Message = $"App '{entity.Name}' was forked",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new
            {
                SourceAppId = sourceAppId,
                SourceAppName = source.Name,
                NewAppId = newId,
                NewAppName = entity.Name,
                request.PublishImmediately
            }
        });


        var newEntity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .FirstAsync(x => x.Id == newId);

        var forked = MapToModel(newEntity, null);
        ApplyStorageMetadata(forked, await fileStorageService.GetAppStorageMetadataAsync(UserId, newId));
        return forked;
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

    public Task SetLocalDevelopmentMetadataAsync(
        Guid appId,
        Guid ownerId,
        bool? frontendSourceAvailable = null,
        string? frontendRuntimeSource = null,
        bool? backendSourceAvailable = null,
        string? backendRuntimeSource = null)
    {
        return fileStorageService.UpdateAppStorageMetadataAsync(ownerId, appId, metadata =>
        {
            metadata.DevelopmentMode = AppDevelopmentModes.Local;
            if (frontendSourceAvailable.HasValue)
            {
                metadata.Frontend.SourceAvailable = frontendSourceAvailable.Value;
            }
            if (!string.IsNullOrWhiteSpace(frontendRuntimeSource))
            {
                metadata.Frontend.RuntimeSource = frontendRuntimeSource;
            }
            if (backendSourceAvailable.HasValue)
            {
                metadata.Backend.SourceAvailable = backendSourceAvailable.Value;
            }
            if (!string.IsNullOrWhiteSpace(backendRuntimeSource))
            {
                metadata.Backend.RuntimeSource = backendRuntimeSource;
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

    internal static void ApplyStorageMetadata(Models.AppModel app, Models.AppStorageMetadata? metadata)
    {
        if (app == null || metadata == null)
        {
            return;
        }

        app.DevelopmentMode = metadata.DevelopmentMode;
        app.Frontend = new Models.AppComponentOwnershipModel
        {
            SourceAvailable = metadata.Frontend.SourceAvailable,
            RuntimeSource = metadata.Frontend.RuntimeSource
        };
        app.Backend = new Models.AppComponentOwnershipModel
        {
            SourceAvailable = metadata.Backend.SourceAvailable,
            RuntimeSource = metadata.Backend.RuntimeSource
        };
        app.SourceAvailable = app.Frontend.SourceAvailable || app.Backend.SourceAvailable;
        app.RuntimeSource = app.Frontend.RuntimeSource == app.Backend.RuntimeSource
            ? app.Frontend.RuntimeSource
            : $"{app.Frontend.RuntimeSource}|{app.Backend.RuntimeSource}";
    }

    private async Task<string> LoadDefaultFrontendTemplateAsync()
    {
        var templatePath = Path.Combine(
            webHostEnvironment.ContentRootPath,
            "Resources",
            "AppTemplates",
            "default-frontend-index.html");

        return await File.ReadAllTextAsync(templatePath);
    }
}
