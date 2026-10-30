using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;

namespace Automaton.Api.Services;

/// <summary>Coordinates app creation, edits, and deletion across persistence and app storage.</summary>
public sealed class AppMutationService(
    ApplicationDbContext dataContext,
    UserContextService userContextService,
    ResourceAuthorizationService authorizationService,
    AppFileStorageService fileStorageService,
    IWebHostEnvironment webHostEnvironment,
    AppMutationAuditService mutationAuditService,
    BackendRouteConfigService backendRouteConfigService,
    AppRunnerAssignmentPolicy runnerAssignmentPolicy,
    AppDeletionService appDeletionService)
{
    private Guid UserId => userContextService.GetUserId();
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<AppMutationService>();
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
            ApplicationContractJson = ApplicationContractSerialization.Serialize(ApplicationContractDefaults.Create()),
            Tags = AppTagNormalizer.Serialize(app.Tags)
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
        await InitializeAppFilesAsync(entity);
        await mutationAuditService.RecordCreatedAsync(entity);
        return entity.Id;
    }

    private async Task InitializeAppFilesAsync(Entities.WebApp entity)
    {
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
            metadata.SourceAvailable = true;
            metadata.RuntimeSource = AppRuntimeSources.Studio;
        });

        await fileStorageService.WriteSourceFileAsync(
            UserId,
            entity.Id,
            "frontend/index.html",
            await LoadDefaultFrontendTemplateAsync());

        // Write automaton.json so external AI tools (Copilot, Cursor, Claude) can discover the app ID.
        var automatonConfig = JsonSerializer.Serialize(
            new
            {
                appId = entity.Id,
                name = entity.Name,
                application = ApplicationContractDefaults.Create()
            },
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        await fileStorageService.WriteSourceFileAsync(UserId, entity.Id, "automaton.json", automatonConfig);
    }

    public async Task Update(Guid id, AppModel app)
    {
        // Check if user can update this app (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateAppAsync(id))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update app {id}");
        }

        var entity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .Include(x => x.DeploymentTargets)
            .SingleOrDefaultAsync(x => x.Id == id);

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
        var previousExecutionRunnerId = GetDefaultRunnerId(entity);

        await runnerAssignmentPolicy.AssignAsync(entity, app.ExecutionRunnerId, UserId);
        ApplyUpdatedAppFields(entity, app);

        await dataContext.SaveChangesAsync();

        // Reconcile on every save so a retry repairs a failed route write even
        // when the requested policy already matches the committed database state.
        await backendRouteConfigService.RefreshAsync();

        // Save app content to file system
        await fileStorageService.SaveAppAsync(ownerId, id, app.Definition);

        // Log activity
        await mutationAuditService.RecordUpdatedAsync(
            entity,
            id,
            ownerId,
            new AppMutationSnapshot(
                previousName,
                previousSummary,
                previousDescription,
                previousIsForkable,
                previousPublicUrl,
                previousRequiresAuthentication,
                previousThumbnailUrl,
                previousCategory,
                previousTags,
                previousIsProfileFeatured,
                previousExecutionRunnerId));
    }

    private static void ApplyUpdatedAppFields(Entities.WebApp entity, AppModel app)
    {
        AppVisibilityPolicy.EnsureValidConfiguration(
            app.RuntimeVisibility,
            app.CatalogVisibility,
            app.MarketplacePreviewMode);
        entity.Name = app.Name;
        entity.Summary = app.Summary;
        entity.Description = app.Description;
        entity.IsPublished = app.IsPublished;
        entity.IsForkable = app.IsForkable;
        entity.PublicUrl = app.PublicUrl;
        entity.RequiresAuthentication = app.RequiresAuthentication;
        entity.RuntimeVisibility = app.RuntimeVisibility;
        entity.CatalogVisibility = app.CatalogVisibility;
        entity.MarketplacePreviewMode = app.MarketplacePreviewMode;
        entity.IsProfileFeatured = app.IsProfileFeatured;
        entity.Updated = DateTime.UtcNow;
        // Definition is now empty - content in file system
        entity.Definition = "{}";
        if (app.Application is not null)
        {
            ApplicationContractValidator.EnsureValid(app.Application);
            entity.ApplicationContractJson = ApplicationContractSerialization.Serialize(app.Application);
        }

        // Public profile display fields
        entity.ThumbnailUrl = app.ThumbnailUrl;
        entity.Category = app.Category;
        entity.Tags = AppTagNormalizer.Serialize(app.Tags);
    }

    public async Task Remove(Guid id, bool recoverInterrupted = false, bool abandonOfflineRuntimes = false)
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

        var app = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .SingleOrDefaultAsync(x => x.Id == id);

        if (app == null)
        {
            throw new InvalidOperationException($"App {id} not found");
        }

        var ownerId = ResolveAppOwnerId(app) ?? UserId;

        await appDeletionService.DeleteResourcesAsync(id, app, ownerId, recoverInterrupted, abandonOfflineRuntimes);

        await mutationAuditService.RecordDeletedAsync(app, ownerId, abandonOfflineRuntimes);
    }

    private Guid? ResolveAppOwnerId(Entities.WebApp appEntity)
    {
        if (appEntity.ProjectId.HasValue)
        {
            var projectOwnerId = dataContext.Projects
                .Where(project => project.Id == appEntity.ProjectId.Value)
                .Select(project => (Guid?)project.OwnerId)
                .FirstOrDefault();
            if (projectOwnerId.HasValue)
                return projectOwnerId.Value;
        }

        var linkedUserIds = dataContext.WebAppUsers
            .Where(link => link.WebAppId == appEntity.Id)
            .Select(link => link.UserId)
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

    private static Guid? GetDefaultRunnerId(Entities.WebApp app)
        => app.DeploymentTargets?.SingleOrDefault(target => target.IsDefault)?.ExecutionRunnerId;
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
