using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Creates app copies and marketplace forks with their source files and activity records.</summary>
public sealed class AppForkingService(
    ApplicationDbContext dataContext,
    UserContextService userContextService,
    AppFileStorageService fileStorageService,
    AppRunnerAssignmentPolicy runnerAssignmentPolicy,
    RecentActivityService recentActivityService,
    IAuditEventService auditEventService)
{
    private Guid UserId => userContextService.GetUserId();
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
            ApplicationContractJson = CreateCopyApplicationContractJson(source.ApplicationContractJson),
            IsPublished = false,
            PublicUrl = null,
            PublishedAt = null,
            Version = 1,
            RequiresAuthentication = source.RequiresAuthentication,
            Scope = AccessScope.Personal,
            ProjectId = null,
            IsSample = false,
            IsFeatured = false,
            IsProfileFeatured = false,
            ThumbnailUrl = source.ThumbnailUrl,
            Category = source.Category,
            Tags = AppTagNormalizer.Serialize(source.Tags),
            Created = now,
            Updated = now,
        };

        dataContext.WebApps.Add(entity);
        dataContext.WebAppUsers.Add(new Entities.WebAppUser { WebAppId = newId, UserId = UserId });
        await runnerAssignmentPolicy.AssignAsync(entity, null, UserId);
        await dataContext.SaveChangesAsync();

        await CopySourceAndRecordDuplicationAsync(appId, newId, entity.Name);

        var newEntity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .FirstAsync(x => x.Id == newId);

        var duplicated = AppsService.MapToModel(newEntity, null, UserId);
        AppsService.ApplyStorageMetadata(duplicated, await fileStorageService.GetAppStorageMetadataAsync(UserId, newId));
        return duplicated;
    }

    private async Task CopySourceAndRecordDuplicationAsync(Guid sourceAppId, Guid duplicateAppId, string appName)
    {
        await fileStorageService.CopySourceFilesAsync(UserId, sourceAppId, UserId, duplicateAppId);
        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, appName, duplicateAppId);
        await auditEventService.WriteAsync(
            action: "Duplicated",
            entityType: "App",
            entityId: duplicateAppId,
            message: $"App '{appName}' was duplicated",
            metadata: new { SourceAppId = sourceAppId, NewAppId = duplicateAppId, UserId });
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

        var hasApplicationSource = fileStorageService.HasAnySourceEntries(sourceOwner, sourceAppId);

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
            HasApplicationSource = hasApplicationSource,
            Application = DeserializeCopyApplicationContract(source.ApplicationContractJson)
        };
    }

    /// <summary>
    /// Fork a published marketplace app into the current user's account.
    /// </summary>
    public async Task<AppModel> ForkAsync(Guid sourceAppId, ForkAppRequest? request = null)
    {
        request ??= new Models.ForkAppRequest();
        var sourceInfo = await LoadForkSourceAsync(sourceAppId);
        var source = sourceInfo.Source;
        var sourceOwner = sourceInfo.OwnerId;

        var newId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var appName = NormalizeForkName(request.Name, source.Name);
        var entity = new Entities.WebApp
        {
            Id = newId,
            Name = appName,
            Summary = source.Summary,
            Description = source.Description,
            Definition = "{}",
            ApplicationContractJson = CreateCopyApplicationContractJson(source.ApplicationContractJson),
            IsPublished = false,
            IsForkable = false,
            PublicUrl = null,
            PublishedAt = null,
            Version = 1,
            RequiresAuthentication = false,
            Scope = AccessScope.Personal,
            ProjectId = null,
            IsSample = false,
            IsFeatured = false,
            IsProfileFeatured = false,
            ThumbnailUrl = source.ThumbnailUrl,
            Category = source.Category,
            Tags = AppTagNormalizer.Serialize(source.Tags),
            ForkedFromAppId = source.Id,
            ForkedFromAuthorUserId = sourceOwner,
            ForkedFromAppName = source.Name,
            ForkedFromAuthorName = sourceInfo.CreatorDisplayName ?? "Automaton creator",
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

        await CopySourceAndRecordForkAsync(sourceOwner, sourceAppId, entity);

        var newEntity = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .FirstAsync(x => x.Id == newId);

        var forked = AppsService.MapToModel(newEntity, null, UserId);
        AppsService.ApplyStorageMetadata(forked, await fileStorageService.GetAppStorageMetadataAsync(UserId, newId));
        return forked;
    }

    private async Task<ForkSourceInfo> LoadForkSourceAsync(Guid sourceAppId)
    {
        var source = await dataContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.WebAppScreenshots)
            .FirstOrDefaultAsync(x =>
                x.Id == sourceAppId &&
                x.IsPublished &&
                x.ReviewStatus == AppReviewStatuses.Approved &&
                x.AccessStatus == AppAccessStatuses.Active)
            ?? throw new InvalidOperationException("Fork source app not found.");
        if (!source.IsForkable)
            throw new InvalidOperationException("Forking is disabled for this app.");
        var ownerId = source.WebAppUsers.FirstOrDefault()?.UserId
            ?? throw new InvalidOperationException("Fork source app has no owner.");
        if (!fileStorageService.AppSourceExists(ownerId, sourceAppId))
            throw new InvalidOperationException("Fork source app has no source files.");
        var creatorDisplayName = await dataContext.Users
            .Where(user => user.Id == ownerId)
            .Select(user => !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Name)
            .FirstOrDefaultAsync();
        return new ForkSourceInfo(source, ownerId, creatorDisplayName);
    }

    private async Task CopySourceAndRecordForkAsync(Guid sourceOwnerId, Guid sourceAppId, Entities.WebApp fork)
    {
        await fileStorageService.CopySourceFilesAsync(sourceOwnerId, sourceAppId, UserId, fork.Id);
        await recentActivityService.LogAppActivityAsync(ActivityType.AppCreated, fork.Name, fork.Id);
        await auditEventService.WriteAsync(new Models.AuditEventWriteRequest
        {
            Action = AuditActions.Forked,
            EntityType = AuditEntityTypes.App,
            EntityId = fork.Id,
            EventType = AuditEventTypes.AppForked,
            Message = $"App '{fork.Name}' was forked",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new
            {
                SourceAppId = sourceAppId,
                SourceAppName = fork.ForkedFromAppName,
                NewAppId = fork.Id,
                NewAppName = fork.Name
            }
        });
    }

    private sealed record ForkSourceInfo(Entities.WebApp Source, Guid OwnerId, string? CreatorDisplayName);

    private static string CreateCopyApplicationContractJson(string contractJson) =>
        ApplicationContractSerialization.Serialize(DeserializeCopyApplicationContract(contractJson));

    private static ApplicationContract DeserializeCopyApplicationContract(string contractJson)
    {
        var contract = ApplicationContractSerialization.Deserialize(contractJson);
        ClearEnvironment(contract.Build.Command);
        ClearEnvironment(contract.Runtime.Command);
        if (contract.Compose is not null)
        {
            foreach (var service in contract.Compose.Services)
                service.Environment.Clear();
        }
        return contract;
    }

    private static void ClearEnvironment(ApplicationCommandSpec? command) => command?.Environment.Clear();

    private static string NormalizeForkName(string? requestedName, string sourceName)
    {
        var name = requestedName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"{sourceName} Remix";
        }

        return name.Length > 200 ? name[..200] : name;
    }

}
