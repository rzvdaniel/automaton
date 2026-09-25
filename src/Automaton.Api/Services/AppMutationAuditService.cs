using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;

namespace Automaton.Api.Services;

/// <summary>Records the activity and audit events produced by app mutations.</summary>
public sealed class AppMutationAuditService(
    UserContextService userContextService,
    RecentActivityService recentActivityService,
    IAuditEventService auditEventService)
{
    private Guid UserId => userContextService.GetUserId();

    public async Task RecordCreatedAsync(WebApp app)
    {
        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, app.Name, app.Id);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.App,
            EntityId = app.Id,
            EventType = AuditEventTypes.AppCreated,
            Message = $"App '{app.Name}' was created",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new { app.Name, ExecutionRunnerId = GetDefaultRunnerId(app) }
        });
    }

    internal async Task RecordUpdatedAsync(WebApp app, Guid appId, Guid ownerId, AppMutationSnapshot previous)
    {
        await recentActivityService.LogAppActivityAsync(ActivityType.AppUpdated, app.Name, appId);

        if (!string.Equals(previous.Name, app.Name, StringComparison.Ordinal))
        {
            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Renamed,
                EntityType = AuditEntityTypes.App,
                EntityId = appId,
                EventType = AuditEventTypes.AppRenamed,
                Message = $"App was renamed from '{previous.Name}' to '{app.Name}'",
                Outcome = AuditOutcomes.Success,
                ActorUserId = UserId,
                Metadata = new { PreviousName = previous.Name, app.Name, OwnerUserId = ownerId }
            });
        }

        if (!string.Equals(previous.PublicUrl, app.PublicUrl, StringComparison.Ordinal))
        {
            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Changed,
                EntityType = AuditEntityTypes.App,
                EntityId = appId,
                EventType = AuditEventTypes.AppSlugChanged,
                Message = $"App slug changed for '{app.Name}'",
                Outcome = AuditOutcomes.Success,
                ActorUserId = UserId,
                Metadata = new { PreviousSlug = previous.PublicUrl, NewSlug = app.PublicUrl, OwnerUserId = ownerId }
            });
        }

        var metadataChanged =
            !string.Equals(previous.Summary, app.Summary, StringComparison.Ordinal) ||
            !string.Equals(previous.Description, app.Description, StringComparison.Ordinal) ||
            previous.IsForkable != app.IsForkable ||
            previous.RequiresAuthentication != app.RequiresAuthentication ||
            !string.Equals(previous.ThumbnailUrl, app.ThumbnailUrl, StringComparison.Ordinal) ||
            !string.Equals(previous.Category, app.Category, StringComparison.Ordinal) ||
            !string.Equals(previous.Tags, app.Tags, StringComparison.Ordinal) ||
            previous.IsProfileFeatured != app.IsProfileFeatured ||
            previous.ExecutionRunnerId != GetDefaultRunnerId(app);

        if (metadataChanged)
        {
            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Updated,
                EntityType = AuditEntityTypes.App,
                EntityId = appId,
                EventType = AuditEventTypes.AppMetadataUpdated,
                Message = $"App metadata was updated for '{app.Name}'",
                Outcome = AuditOutcomes.Success,
                ActorUserId = UserId,
                Metadata = new
                {
                    app.Name,
                    app.PublicUrl,
                    app.IsForkable,
                    app.RequiresAuthentication,
                    app.ThumbnailUrl,
                    app.Category,
                    app.Tags,
                    app.IsProfileFeatured,
                    ExecutionRunnerId = GetDefaultRunnerId(app),
                    OwnerUserId = ownerId
                }
            });
        }
    }

    public async Task RecordDeletedAsync(WebApp app, Guid ownerId)
    {
        await recentActivityService.LogAppActivityAsync(ActivityType.AppDeleted, app.Name, app.Id);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Deleted,
            EntityType = AuditEntityTypes.App,
            EntityId = app.Id,
            EventType = AuditEventTypes.AppDeleted,
            Message = $"App '{app.Name}' was deleted",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new
            {
                AppId = app.Id,
                Name = app.Name,
                app.PublicUrl,
                ExecutionRunnerId = GetDefaultRunnerId(app),
                OwnerUserId = ownerId
            }
        });
    }

    private static Guid? GetDefaultRunnerId(WebApp app)
        => app.DeploymentTargets?.SingleOrDefault(target => target.IsDefault)?.ExecutionRunnerId;
}

internal sealed record AppMutationSnapshot(
    string Name,
    string? Summary,
    string? Description,
    bool IsForkable,
    string? PublicUrl,
    bool RequiresAuthentication,
    string? ThumbnailUrl,
    string? Category,
    string? Tags,
    bool IsProfileFeatured,
    Guid? ExecutionRunnerId);
