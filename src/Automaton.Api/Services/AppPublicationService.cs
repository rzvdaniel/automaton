using Automaton.Api.Constants;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Publishes and unpublishes apps, maintaining runner assignment, audit, activity, and route state.
/// </summary>
public sealed class AppPublicationService(
    ApplicationDbContext dataContext,
    UserContextService userContextService,
    ResourceAuthorizationService authorizationService,
    AppRunnerAssignmentPolicy runnerAssignmentPolicy,
    IAuditEventService auditEventService,
    IApplicationEventService applicationEventService,
    RecentActivityService recentActivityService,
    BackendRouteConfigService backendRouteConfigService,
    MachineStaticAppPublicationService? machineStaticPublication = null)
{
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<AppPublicationService>();
    private Guid UserId => userContextService.GetUserId();

    public async Task Publish(
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
    {
        if (!await authorizationService.CanUpdateAppAsync(id))
            throw new UnauthorizedAccessException($"You do not have permission to publish app {id}");

        var entity = await dataContext.WebApps
            .Include(app => app.WebAppUsers)
            .SingleOrDefaultAsync(app => app.Id == id, cancellationToken);
        if (entity == null)
            throw new InvalidOperationException($"App {id} not found");

        if (!PublishedAppHostNames.IsValidPublishedSlug(publicUrl))
            throw new InvalidOperationException("Invalid URL slug format. Use lowercase letters, numbers, and hyphens only.");

        AppVisibilityPolicy.EnsureValidConfiguration(runtimeVisibility, catalogVisibility, marketplacePreviewMode);
        if (await dataContext.WebApps.AnyAsync(app => app.PublicUrl == publicUrl && app.Id != id, cancellationToken))
            throw new InvalidOperationException("This URL is already taken");

        if (executionRunnerId.HasValue)
            await runnerAssignmentPolicy.AssignAsync(entity, executionRunnerId, UserId);

        var isMachineStaticPublication = machineStaticPublication is not null
            && await machineStaticPublication.PublishIfAssignedAsync(entity, runtimeVisibility, UserId, cancellationToken);
        if (runtimeVisibility == AppRuntimeVisibilities.Public && requiresAuthentication && !isMachineStaticPublication)
        {
            throw new InvalidOperationException(
                "Restricted published apps require a Machine Management Runner static publication. Use Studio preview for unsupported Runner profiles.");
        }

        var ownerId = ResolveAppOwnerId(entity) ?? UserId;
        entity.IsPublished = true;
        entity.RuntimeVisibility = runtimeVisibility;
        entity.CatalogVisibility = catalogVisibility;
        entity.MarketplacePreviewMode = marketplacePreviewMode;
        entity.PublicUrl = publicUrl;
        entity.PublishedAt = DateTime.UtcNow;
        entity.IsForkable = isForkable;
        entity.RequiresAuthentication = requiresAuthentication;
        entity.Category = category?.Trim();
        entity.Tags = AppTagNormalizer.Serialize(tags);
        entity.Version++;
        entity.Updated = DateTime.UtcNow;

        await dataContext.SaveChangesAsync(cancellationToken);
        await RecordPublishedAppAsync(entity, id, ownerId, publicUrl, requiresAuthentication, isForkable);
    }

    public async Task Unpublish(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await authorizationService.CanUpdateAppAsync(id))
            throw new UnauthorizedAccessException($"You do not have permission to unpublish app {id}");

        var entity = await dataContext.WebApps
            .Include(app => app.DeploymentTargets)
            .SingleOrDefaultAsync(app => app.Id == id, cancellationToken);
        if (entity == null)
            throw new InvalidOperationException($"App {id} not found");

        entity.IsPublished = false;
        entity.RuntimeVisibility = AppRuntimeVisibilities.Private;
        entity.CatalogVisibility = AppCatalogVisibilities.Unlisted;
        entity.PublishedAt = null;
        entity.Updated = DateTime.UtcNow;
        // Keep PublicUrl for re-publishing with the same slug.
        await dataContext.SaveChangesAsync(cancellationToken);
        await RecordUnpublishedAppAsync(entity, id);
        if (machineStaticPublication is not null)
            await machineStaticPublication.QueueRemovalIfAssignedAsync(id, UserId, cancellationToken);
    }

    public async Task UpdateRunner(Guid id, Guid? executionRunnerId)
    {
        if (!await authorizationService.CanUpdateAppAsync(id))
            throw new UnauthorizedAccessException($"You do not have permission to update app {id}");

        var entity = dataContext.WebApps.SingleOrDefault(app => app.Id == id);
        if (entity == null)
            throw new InvalidOperationException($"App {id} not found");

        var previousRunnerId = GetDefaultRunnerId(entity);
        await runnerAssignmentPolicy.AssignAsync(entity, executionRunnerId, UserId);
        entity.Updated = DateTime.UtcNow;
        await dataContext.SaveChangesAsync();

        await auditEventService.WriteAsync(new AuditEventWriteRequest
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
            Metadata = new { AppId = id, PreviousRunnerId = previousRunnerId, RunnerId = executionRunnerId }
        });

        await backendRouteConfigService.RefreshAsync();
    }

    private async Task RecordPublishedAppAsync(
        WebApp entity,
        Guid appId,
        Guid ownerId,
        string publicUrl,
        bool requiresAuthentication,
        bool isForkable)
    {
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Published,
            EntityType = AuditEntityTypes.App,
            EntityId = appId,
            EventType = AuditEventTypes.AppPublished,
            Message = $"App '{entity.Name}' was published",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new
            {
                Slug = publicUrl,
                entity.Version,
                RequiresAuthentication = requiresAuthentication,
                RunnerId = GetDefaultRunnerId(entity),
                IsForkable = isForkable,
                OwnerUserId = ownerId
            }
        });

        await applicationEventService.WriteAsync(new ApplicationEventWriteRequest
        {
            AppId = appId,
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
                RunnerId = GetDefaultRunnerId(entity),
                IsForkable = isForkable
            }
        });

        await recentActivityService.LogAppActivityAsync(ActivityType.AppPublished, entity.Name, appId);
        await backendRouteConfigService.RefreshAsync();
    }

    private async Task RecordUnpublishedAppAsync(WebApp entity, Guid appId)
    {
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Unpublished,
            EntityType = AuditEntityTypes.App,
            EntityId = appId,
            EventType = AuditEventTypes.AppUnpublished,
            Message = $"App '{entity.Name}' was unpublished",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new { entity.PublicUrl, entity.Version }
        });

        await applicationEventService.WriteAsync(new ApplicationEventWriteRequest
        {
            AppId = appId,
            Level = ApplicationEventLevels.Info,
            Category = ApplicationEventCategories.Publishing,
            EventType = ApplicationEventTypes.AppUnpublished,
            Message = "Application unpublished.",
            ActorUserId = UserId,
            Metadata = new { entity.PublicUrl, entity.Version }
        });

        await recentActivityService.LogAppActivityAsync(ActivityType.AppUnpublished, entity.Name, appId);
        await backendRouteConfigService.RefreshAsync();
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

    private static Guid? GetDefaultRunnerId(WebApp app)
        => app.DeploymentTargets?.SingleOrDefault(target => target.IsDefault)?.ExecutionRunnerId;
}
