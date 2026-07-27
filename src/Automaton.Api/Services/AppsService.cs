using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Data;

namespace Automaton.Api.Services;

public class AppsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly RecentActivityService recentActivityService;
    private readonly UserContextService userContextService;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly AppFileStorageService fileStorageService;
    private readonly IWebHostEnvironment webHostEnvironment;
    private readonly IAuditEventService auditEventService;
    private readonly IApplicationEventService applicationEventService;
    private readonly BackendRouteConfigService backendRouteConfigService;
    private readonly AppRunnerAssignmentPolicy runnerAssignmentPolicy;
    private readonly Serilog.ILogger logger;

    // ✅ Helper property to get current userId (respects impersonation)
    private Guid UserId => userContextService.GetUserId();

    public AppsService(
        ApplicationDbContext dataContext,
        UserContextService userContextService,
        RecentActivityService recentActivityService,
        ResourceAuthorizationService authorizationService,
        AppFileStorageService fileStorageService,
        IWebHostEnvironment webHostEnvironment,
        IAuditEventService auditEventService,
        IApplicationEventService applicationEventService,
        BackendRouteConfigService backendRouteConfigService,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy
    )
    {
        this.dataContext = dataContext;
        this.userContextService = userContextService;
        this.recentActivityService = recentActivityService;
        this.authorizationService = authorizationService;
        this.fileStorageService = fileStorageService;
        this.webHostEnvironment = webHostEnvironment;
        this.auditEventService = auditEventService;
        this.applicationEventService = applicationEventService;
        this.backendRouteConfigService = backendRouteConfigService;
        this.runnerAssignmentPolicy = runnerAssignmentPolicy;
        this.logger = Serilog.Log.ForContext<AppsService>();
    }

    public async Task<IReadOnlyList<AppListItemDto>> ListAsync(Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var rows = await authorizationService.GetAppsQueryByContext(projectId)
            .AsNoTracking()
            .Select(app => new
            {
                app.Id, app.Name, app.Description, app.ThumbnailUrl, app.Tags,
                app.IsPublished, app.PublicUrl, app.Updated, app.ProjectId,
                OwnerId = app.ProjectId.HasValue
                    ? dataContext.Projects.Where(p => p.Id == app.ProjectId.Value).Select(p => (Guid?)p.OwnerId).FirstOrDefault()
                    : app.WebAppUsers.OrderBy(u => u.UserId).Select(u => (Guid?)u.UserId).FirstOrDefault(),
                Runner = dataContext.Runners.Where(r => r.Id == app.ExecutionRunnerId)
                    .Select(r => new AppListRunnerDto
                    {
                        Id = r.Id, Name = r.Name, Purpose = r.Purpose,
                        ConnectivityStatus = RunnerPresenceEvaluator.DeriveStatus(r.ConnectionId, r.LastHeartbeatAtUtc, now)
                    }).FirstOrDefault()
            }).ToListAsync(cancellationToken);

        using var metadataGate = new SemaphoreSlim(8);
        var items = await Task.WhenAll(rows.Select(async row =>
        {
            var ownerId = row.OwnerId ?? UserId;
            await metadataGate.WaitAsync(cancellationToken);
            AppStorageMetadata metadata;
            try
            {
                metadata = await fileStorageService.GetAppStorageMetadataAsync(ownerId, row.Id);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                logger.Warning(exception, "Unable to load app storage metadata for {AppId}; treating runtime as unavailable", row.Id);
                metadata = new AppStorageMetadata { Frontend = new(), Backend = new() };
            }
            finally
            {
                metadataGate.Release();
            }
            return new AppListItemDto
            {
                Id = row.Id, Name = row.Name, Description = row.Description ?? string.Empty,
                ThumbnailUrl = row.ThumbnailUrl, Tags = ParseTags(row.Tags), IsPublished = row.IsPublished,
                PublicUrl = row.PublicUrl, UpdatedAtUtc = row.Updated,
                CanDelete = !row.ProjectId.HasValue,
                Runner = row.Runner,
                Runtime = new AppListRuntimeDto
                {
                    FrontendAvailable = metadata.Frontend.RuntimeAvailable,
                    BackendAvailable = metadata.Backend.RuntimeAvailable,
                    FrontendDeployedAtUtc = metadata.Frontend.RuntimeDeployedAtUtc,
                    BackendDeployedAtUtc = metadata.Backend.RuntimeDeployedAtUtc
                }
            };
        }));
        return items;
    }

    // Legacy projection retained for MCP callers until their contract is migrated.
    public IEnumerable<AppModel> ListForMcp(Guid? projectId = null)
    {
        return authorizationService.GetAppsQueryByContext(projectId)
            .Include(x => x.WebAppUsers).Include(x => x.WebAppScreenshots).AsSplitQuery().ToList()
            .Select(e => MapToModel(e, null, ResolveAppOwnerId(e) ?? UserId));
    }

    internal static IReadOnlyList<string> ParseTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags) ? Array.Empty<string>() : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public AppModel Get(Guid id)
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

        var app = MapToModel(appEntity, definition, ownerId);
        ApplyStorageMetadata(app, fileStorageService.GetAppStorageMetadataAsync(ownerId, id).Result);

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

        var ownerId = await ResolveAppOwnerIdAsync(appEntity, cancellationToken);

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
    public async Task<PublishedAppInfo?> GetAccessibleAppInfoByIdAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        return await GetAccessibleAppInfoByIdAsync(appId, UserId, cancellationToken);
    }

    public async Task<PublishedAppInfo?> GetAccessibleAppInfoByIdAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        var appEntity = await authorizationService.GetAccessibleAppsQuery(userId)
            .Include(x => x.WebAppUsers)
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
            ExecutionRunnerId = appEntity.ExecutionRunnerId
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

    public async Task<Guid> Create(NewApp app)
    {
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
            ExecutionRunnerId = null
        };

        dataContext.WebApps.Add(entity);

        var appUser = new Entities.WebAppUser
        {
            WebAppId = entity.Id,
            UserId = UserId
        };

        dataContext.WebAppUsers.Add(appUser);
        await runnerAssignmentPolicy.AssignAsync(entity, app.ExecutionRunnerId, UserId);
        await dataContext.SaveChangesAsync();

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

    public async Task Update(Guid id, AppModel app)
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
        var ownerId = ResolveAppOwnerId(entity) ?? UserId;
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

        await runnerAssignmentPolicy.AssignAsync(entity, app.ExecutionRunnerId, UserId);
        entity.Name = app.Name;
        entity.Summary = app.Summary;
        entity.Description = app.Description;
        entity.IsPublished = app.IsPublished;
        entity.IsForkable = app.IsForkable;
        entity.PublicUrl = app.PublicUrl;
        entity.RequiresAuthentication = app.RequiresAuthentication;
        entity.IsProfileFeatured = app.IsProfileFeatured;
        entity.Updated = DateTime.UtcNow;
        // Definition is now empty - content in file system
        entity.Definition = "{}";

        // Public profile display fields
        entity.ThumbnailUrl = app.ThumbnailUrl;
        entity.Category = app.Category;
        entity.Tags = app.Tags;

        await dataContext.SaveChangesAsync();

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

        if (previousExecutionRunnerId != entity.ExecutionRunnerId)
        {
            await backendRouteConfigService.RefreshAsync();
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
        var ownerId = ResolveAppOwnerId(app) ?? UserId;

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

        await backendRouteConfigService.RefreshAsync();

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

        if (executionRunnerId.HasValue)
        {
            await runnerAssignmentPolicy.AssignAsync(entity, executionRunnerId, UserId);
        }

        var ownerId = ResolveAppOwnerId(entity) ?? UserId;
        var nextVersion = entity.Version + 1;

        entity.IsPublished = true;
        entity.PublicUrl = publicUrl;
        entity.PublishedAt = DateTime.UtcNow;
        entity.IsForkable = isForkable;
        entity.RequiresAuthentication = requiresAuthentication;
        entity.Category = category?.Trim();
        entity.Tags = tags?.Trim();
        entity.Version = nextVersion;
        entity.Updated = DateTime.UtcNow;

        await dataContext.SaveChangesAsync();

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

        await applicationEventService.WriteAsync(new ApplicationEventWriteRequest
        {
            AppId = id,
            Level = ApplicationEventLevels.Info,
            Category = ApplicationEventCategories.Publishing,
            EventType = ApplicationEventTypes.AppPublished,
            Message = "Application published.",
            ActorUserId = UserId,
            Metadata = new
            {
                Slug = publicUrl,
                entity.Version,
                RequiresAuthentication = requiresAuthentication,
                RunnerId = entity.ExecutionRunnerId,
                IsForkable = isForkable
            }
        });


        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppPublished, entity.Name, id);
        await backendRouteConfigService.RefreshAsync();
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

        await applicationEventService.WriteAsync(new ApplicationEventWriteRequest
        {
            AppId = id,
            Level = ApplicationEventLevels.Info,
            Category = ApplicationEventCategories.Publishing,
            EventType = ApplicationEventTypes.AppUnpublished,
            Message = "Application unpublished.",
            ActorUserId = UserId,
            Metadata = new
            {
                entity.PublicUrl,
                entity.Version
            }
        });


        // Log activity
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUnpublished, entity.Name, id);
        await backendRouteConfigService.RefreshAsync();
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

        await runnerAssignmentPolicy.AssignAsync(entity, executionRunnerId, UserId);
        entity.Updated = DateTime.UtcNow;

        await dataContext.SaveChangesAsync();

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

        await backendRouteConfigService.RefreshAsync();

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
            Created = entity.Created,
            Updated = entity.Updated,
            ExecutionRunnerId = entity.ExecutionRunnerId,
            // Project context
            Scope = entity.Scope,
            ProjectId = entity.ProjectId,
            Definition = definition ?? JsonSerializer.Deserialize<AppDefinition>(entity.Definition ?? "{}") ?? new Models.AppDefinition(),
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
    public async Task<AppModel> DuplicateAsync(Guid appId)
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
            ExecutionRunnerId = null,
            RequiresAuthentication = source.RequiresAuthentication,
            Scope = AccessScope.Personal,
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
        await runnerAssignmentPolicy.AssignAsync(entity, source.ExecutionRunnerId, UserId);
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

        var duplicated = MapToModel(newEntity, null, UserId);
        ApplyStorageMetadata(duplicated, await fileStorageService.GetAppStorageMetadataAsync(UserId, newId));
        return duplicated;
    }

    /// <summary>
    /// Get metadata for the guided fork screen.
    /// </summary>
    public async Task<ForkAppSourceModel> GetForkSourceAsync(Guid sourceAppId)
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
    public async Task<AppModel> ForkAsync(Guid sourceAppId, ForkAppRequest? request = null)
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
            Scope = AccessScope.Personal,
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

        var forked = MapToModel(newEntity, null, UserId);
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
    public async Task<IEnumerable<AppModel>> GetSampleAppsAsync()
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
        bool? frontendRuntimeAvailable = null,
        bool? backendSourceAvailable = null,
        string? backendRuntimeSource = null,
        bool? backendRuntimeAvailable = null)
    {
        return fileStorageService.UpdateAppStorageMetadataAsync(ownerId, appId, metadata =>
        {
            metadata.DevelopmentMode = AppDevelopmentModes.Local;
            if (frontendSourceAvailable.HasValue)
            {
                metadata.Frontend.SourceAvailable = frontendSourceAvailable.Value;
                if (frontendSourceAvailable.Value && metadata.Frontend.SourceModifiedAtUtc == null)
                {
                    metadata.Frontend.SourceModifiedAtUtc = DateTime.UtcNow;
                }
            }
            if (!string.IsNullOrWhiteSpace(frontendRuntimeSource))
            {
                metadata.Frontend.RuntimeSource = frontendRuntimeSource;
            }
            if (frontendRuntimeAvailable.HasValue)
            {
                metadata.Frontend.RuntimeAvailable = frontendRuntimeAvailable.Value;
                if (frontendRuntimeAvailable.Value && metadata.Frontend.RuntimeDeployedAtUtc == null)
                {
                    metadata.Frontend.RuntimeDeployedAtUtc = DateTime.UtcNow;
                }
            }
            if (backendSourceAvailable.HasValue)
            {
                metadata.Backend.SourceAvailable = backendSourceAvailable.Value;
                if (backendSourceAvailable.Value && metadata.Backend.SourceModifiedAtUtc == null)
                {
                    metadata.Backend.SourceModifiedAtUtc = DateTime.UtcNow;
                }
            }
            if (!string.IsNullOrWhiteSpace(backendRuntimeSource))
            {
                metadata.Backend.RuntimeSource = backendRuntimeSource;
            }
            if (backendRuntimeAvailable.HasValue)
            {
                metadata.Backend.RuntimeAvailable = backendRuntimeAvailable.Value;
                if (backendRuntimeAvailable.Value && metadata.Backend.RuntimeDeployedAtUtc == null)
                {
                    metadata.Backend.RuntimeDeployedAtUtc = DateTime.UtcNow;
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

    internal static void ApplyStorageMetadata(AppModel app, AppStorageMetadata? metadata)
    {
        if (app == null || metadata == null)
        {
            return;
        }

        app.DevelopmentMode = metadata.DevelopmentMode;
        app.Frontend = new Models.AppComponentOwnershipModel
        {
            SourceAvailable = metadata.Frontend.SourceAvailable,
            SourceModifiedAtUtc = metadata.Frontend.SourceModifiedAtUtc,
            RuntimeSource = metadata.Frontend.RuntimeSource,
            RuntimeAvailable = metadata.Frontend.RuntimeAvailable,
            RuntimeDeployedAtUtc = metadata.Frontend.RuntimeDeployedAtUtc
        };
        app.Backend = new Models.AppComponentOwnershipModel
        {
            SourceAvailable = metadata.Backend.SourceAvailable,
            SourceModifiedAtUtc = metadata.Backend.SourceModifiedAtUtc,
            RuntimeSource = metadata.Backend.RuntimeSource,
            RuntimeAvailable = metadata.Backend.RuntimeAvailable,
            RuntimeDeployedAtUtc = metadata.Backend.RuntimeDeployedAtUtc
        };
        app.SourceAvailable = app.Frontend.SourceAvailable || app.Backend.SourceAvailable;
        app.RuntimeSource = app.Frontend.RuntimeSource == app.Backend.RuntimeSource
            ? app.Frontend.RuntimeSource
            : $"{app.Frontend.RuntimeSource}|{app.Backend.RuntimeSource}";
        app.HasDeployedFrontend = metadata.Frontend.RuntimeAvailable;
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
