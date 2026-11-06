using Automaton.Api.Constants;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Queries and maps the app list read model.</summary>
internal sealed class AppListingService(
    ApplicationDbContext dataContext,
    ResourceAuthorizationService authorizationService)
{
    public async Task<IReadOnlyList<AppListItemDto>> ListAsync(
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rows = await LoadRowsAsync(projectId, cancellationToken);
        var items = rows.Select(row => MapRow(row, now)).ToArray();
        // Only Action-only apps show their last run, so look it up for those apps alone.
        for (var index = 0; index < items.Length; index++)
        {
            if (!items[index].ActionsOnly) continue;
            var appId = items[index].Id;
            var run = await dataContext.AppModuleExecutions.AsNoTracking()
                .Where(execution => execution.AppId == appId)
                .OrderByDescending(execution => execution.CreatedAtUtc)
                .Select(execution => new AppListActionRunDto { ModuleKey = execution.ModuleKey, Status = execution.Status, CreatedAtUtc = execution.CreatedAtUtc })
                .FirstOrDefaultAsync(cancellationToken);
            items[index] = items[index].WithLastActionRun(run);
        }

        return items;
    }

    private Task<List<AppListQueryRow>> LoadRowsAsync(
        Guid? projectId,
        CancellationToken cancellationToken)
        => authorizationService.GetAppsQueryByContext(projectId)
            .AsNoTracking()
            .Select(app => new AppListQueryRow
            {
                Id = app.Id,
                Name = app.Name,
                Summary = app.Summary,
                Description = app.Description,
                ThumbnailUrl = app.ThumbnailUrl,
                Tags = app.Tags,
                IsPublished = app.IsPublished,
                PublicUrl = app.PublicUrl,
                Updated = app.Updated,
                ProjectId = app.ProjectId,
                ContractJson = app.ApplicationContractJson,
                CanLaunchPublicly = app.IsPublished &&
                    app.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    app.AccessStatus == AppAccessStatuses.Active,
                Runner = dataContext.Runners
                    .Where(runner => app.DeploymentTargets.Any(target =>
                        target.IsDefault && target.ExecutionRunnerId == runner.Id))
                    .Select(runner => new AppListRunnerRow
                    {
                        Id = runner.Id,
                        Name = runner.Name,
                        Purpose = runner.Purpose,
                        ConnectionId = runner.ConnectionId,
                        LastHeartbeatAtUtc = runner.LastHeartbeatAtUtc
                    })
                    .FirstOrDefault(),
                LiveTarget = app.DeploymentTargets
                    .Where(target => target.IsDefault)
                    .Select(target => new AppListTargetRow
                    {
                        Id = target.Id,
                        Name = target.Name,
                        Status = target.Status,
                        GenericRuntimeDeployed = target.GenericRuntimeDeployed,
                        LastSuccessfulDeploymentAt = target.LastSuccessfulDeploymentAt,
                        IsHostedRunner = target.ExecutionRunnerId.HasValue && dataContext.HostedRunners
                            .Any(hosted => hosted.RunnerId == target.ExecutionRunnerId && hosted.DeletedAt == null),
                        Runner = target.ExecutionRunner == null ? null : new AppListTargetRunnerRow
                        {
                            ApplicationProfile = target.ExecutionRunner.ApplicationProfile,
                            ConnectionId = target.ExecutionRunner.ConnectionId,
                            LastHeartbeatAtUtc = target.ExecutionRunner.LastHeartbeatAtUtc
                        }
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

    private static AppListItemDto MapRow(AppListQueryRow row, DateTime now)
        => new()
        {
            Id = row.Id,
            Name = row.Name,
            Summary = row.Summary ?? string.Empty,
            Description = row.Description ?? string.Empty,
            ThumbnailUrl = row.ThumbnailUrl,
            Tags = AppTagNormalizer.Normalize(row.Tags),
            IsPublished = row.IsPublished,
            PublicUrl = row.PublicUrl,
            CanLaunchPublicly = row.CanLaunchPublicly,
            UpdatedAtUtc = row.Updated,
            CanDelete = !row.ProjectId.HasValue,
            Runner = row.Runner == null ? null : MapRunner(row.Runner, now),
            LiveEnvironment = row.LiveTarget == null ? null : MapLiveTarget(row.Id, row.LiveTarget, now),
            ActionsOnly = IsActionsOnly(row.ContractJson),
            Runtime = new AppListRuntimeDto
            {
                GenericRuntimeDeployed = row.LiveTarget?.GenericRuntimeDeployed == true
            }
        };

    public static bool IsActionsOnly(string? contractJson)
    {
        if (string.IsNullOrWhiteSpace(contractJson)) return false;
        try
        {
            var contract = ApplicationContractSerialization.Deserialize(contractJson);
            return contract.Runtime.Command is null && contract.Compose is null && !contract.IsStaticSite;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static AppListEnvironmentDto MapLiveTarget(Guid appId, AppListTargetRow target, DateTime now)
        => new()
        {
            Name = target.Name,
            Status = target.Runner == null ? "Unassigned" : target.Status.ToString(),
            IsHostedRunner = target.IsHostedRunner,
            ApplicationProfile = target.Runner?.ApplicationProfile,
            RunnerOnline = target.Runner != null && RunnerPresenceEvaluator.DeriveStatus(
                target.Runner.ConnectionId, target.Runner.LastHeartbeatAtUtc, now) == RunnerStatus.Online,
            GenericRuntimeDeployed = target.GenericRuntimeDeployed,
            LastSuccessfulDeploymentAtUtc = target.LastSuccessfulDeploymentAt?.UtcDateTime,
            IsStaticHosting = target.Runner == null && target.GenericRuntimeDeployed,
            RoutePath = PublishedAppHostNames.BuildPrivateTargetPath(appId, Guid.Empty, isDefault: true)
        };

    private static AppListRunnerDto MapRunner(AppListRunnerRow runner, DateTime now)
        => new()
        {
            Id = runner.Id,
            Name = runner.Name,
            Purpose = runner.Purpose,
            ConnectivityStatus = RunnerPresenceEvaluator.DeriveStatus(
                runner.ConnectionId, runner.LastHeartbeatAtUtc, now)
        };

    private sealed class AppListQueryRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public string? ThumbnailUrl { get; init; }
        public string? Tags { get; init; }
        public bool IsPublished { get; init; }
        public string? PublicUrl { get; init; }
        public DateTime Updated { get; init; }
        public Guid? ProjectId { get; init; }
        public bool CanLaunchPublicly { get; init; }
        public string? ContractJson { get; init; }
        public AppListRunnerRow? Runner { get; init; }
        public AppListTargetRow? LiveTarget { get; init; }
    }

    private sealed class AppListTargetRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public DeploymentTargetStatus Status { get; init; }
        public bool GenericRuntimeDeployed { get; init; }
        public DateTimeOffset? LastSuccessfulDeploymentAt { get; init; }
        public bool IsHostedRunner { get; init; }
        public AppListTargetRunnerRow? Runner { get; init; }
    }

    private sealed class AppListTargetRunnerRow
    {
        public string? ApplicationProfile { get; init; }
        public string? ConnectionId { get; init; }
        public DateTime? LastHeartbeatAtUtc { get; init; }
    }

    private sealed class AppListRunnerRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Purpose { get; init; } = string.Empty;
        public string? ConnectionId { get; init; }
        public DateTime? LastHeartbeatAtUtc { get; init; }
    }
}
