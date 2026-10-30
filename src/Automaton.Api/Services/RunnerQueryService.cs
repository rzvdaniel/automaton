using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Mapping;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Reads authorized Runner projections, including project membership, Hosted Runner, and session data.
/// </summary>
public sealed class RunnerQueryService(
    ApplicationDbContext dbContext,
    ResourceAuthorizationService authorizationService,
    UserContextService userContextService,
    RunnerConnectionSessionService connectionSessionService)
{
    private Guid UserId => userContextService.GetUserId();

    public async Task<IEnumerable<RunnerDetails>> List(CancellationToken cancellationToken)
        => await List(null, cancellationToken);

    public async Task<IEnumerable<RunnerDetails>> List(Guid? projectId, CancellationToken cancellationToken)
    {
        var runnerEntities = await authorizationService.GetRunnersQueryByContext(projectId, UserId)
            .Where(runner => runner.Purpose == RunnerPurposes.Managed &&
                runner.ApplicationProfile == RunnerApplicationProfiles.MachineManagement)
            .ToListAsync(cancellationToken);
        var deletedHostedRunners = await dbContext.HostedRunners
            .AsNoTracking()
            .Where(x => x.DeletedAt != null)
            .Select(x => new { x.RunnerId, x.Name })
            .ToListAsync(cancellationToken);
        var deletedHostedRunnerIds = deletedHostedRunners
            .Where(x => x.RunnerId.HasValue)
            .Select(x => x.RunnerId!.Value)
            .ToHashSet();
        var deletedHostedRunnerNames = deletedHostedRunners
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        runnerEntities = runnerEntities
            .Where(runner => !deletedHostedRunnerIds.Contains(runner.Id) && !deletedHostedRunnerNames.Contains(runner.Name))
            .ToList();

        var projectLookup = await BuildRunnerProjectLookup(
            runnerEntities.Select(runner => runner.Id), projectId, UserId, cancellationToken);
        var hostedRunnerLookup = await BuildHostedRunnerLookup(runnerEntities.Select(runner => runner.Id), cancellationToken);
        var results = (await GetRunnersWithUpdatedStatus(
            runnerEntities, projectLookup, hostedRunnerLookup, cancellationToken)).ToList();
        var runnerIds = runnerEntities.Select(runner => runner.Id).ToArray();
        var hostedStorage = await dbContext.HostedRunners.AsNoTracking()
            .Where(x => x.RunnerId != null && runnerIds.Contains(x.RunnerId.Value))
            .ToDictionaryAsync(x => x.RunnerId!.Value, cancellationToken);

        foreach (var result in results)
        {
            if (!hostedStorage.TryGetValue(result.Id, out var hosted))
                continue;

            result.StorageAllowanceBytes = hosted.StorageAllowanceBytes;
            result.StorageTotalBytes = hosted.StorageTotalBytes;
            result.StorageUsagePercent = hosted.StorageUsagePercent;
            result.StorageMeasuredAtUtc = hosted.StorageMeasuredAtUtc;
            result.StorageMeasurementSource = hosted.StorageMeasurementSource;
            result.StorageMeasurementStatus = hosted.StorageMeasurementStatus;
            result.StorageState = hosted.StorageState;
            result.StorageMeasurementError = hosted.StorageMeasurementError;
        }

        return results;
    }

    public Runner Get(Guid id)
    {
        var runner = authorizationService.GetAccessibleRunnersQuery()
            .SingleOrDefault(x => x.Id == id)
            ?? throw new KeyNotFoundException("Runner not found.");
        dbContext.Entry(runner).Reload();
        return runner;
    }

    public async Task<RunnerDetails?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        var runner = await authorizationService.GetAccessibleRunnersQuery()
            .FirstOrDefaultAsync(x => x.Purpose == RunnerPurposes.Managed &&
                x.ApplicationProfile == RunnerApplicationProfiles.MachineManagement &&
                x.Name.ToLower() == name.ToLower(), cancellationToken);
        if (runner == null)
            return null;

        var projectLookup = await BuildRunnerProjectLookup([runner.Id], null, UserId, cancellationToken);
        var hostedRunnerId = await dbContext.HostedRunners
            .AsNoTracking()
            .Where(x => x.RunnerId == runner.Id && x.DeletedAt == null)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var status = RunnerPresenceEvaluator.DeriveStatus(
            runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow);

        RunnerDetails details;
        if (projectLookup.TryGetValue(runner.Id, out var link))
        {
            details = RunnerMapper.ToRunnerDetails(runner, status, link.ProjectId, link.ProjectName, hostedRunnerId);
        }
        else
        {
            details = RunnerMapper.ToRunnerDetails(runner, status, null, null, hostedRunnerId);
        }

        await connectionSessionService.PopulateSessionStatsAsync(details, cancellationToken);
        return details;
    }

    private async Task<Dictionary<Guid, (Guid ProjectId, string ProjectName)>> BuildRunnerProjectLookup(
        IEnumerable<Guid> runnerIds,
        Guid? projectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var ids = runnerIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, (Guid ProjectId, string ProjectName)>();

        var query = dbContext.ProjectRunners
            .Include(x => x.Project)
            .Where(x => ids.Contains(x.RunnerId));
        query = projectId.HasValue
            ? query.Where(x => x.ProjectId == projectId.Value)
            : query.Where(x => x.Project != null && x.Project.Members.Any(member => member.UserId == userId && member.IsActive));

        var links = await query.ToListAsync(cancellationToken);
        return links
            .GroupBy(x => x.RunnerId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var first = group.First();
                    return (first.ProjectId, first.Project?.DisplayName ?? string.Empty);
                });
    }

    private async Task<IEnumerable<RunnerDetails>> GetRunnersWithUpdatedStatus(
        IEnumerable<Runner> runnerEntities,
        IDictionary<Guid, (Guid ProjectId, string ProjectName)> projectLookup,
        IDictionary<Guid, Guid> hostedRunnerLookup,
        CancellationToken cancellationToken)
    {
        var runners = RunnerMapper
            .ToRunnerDetailsList(runnerEntities, RunnerStatus.None, projectLookup, hostedRunnerLookup)
            .ToList();
        var sessionStats = await connectionSessionService.GetSessionStatsAsync(
            runners.Select(runner => runner.Id), cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var runner in runners)
        {
            runner.Status = RunnerPresenceEvaluator.DeriveStatus(runner.ConnectionId, runner.LastHeartbeatAtUtc, now);
            if (!sessionStats.TryGetValue(runner.Id, out var stats))
                continue;

            runner.CurrentSessionConnectedAtUtc = stats.CurrentSessionConnectedAtUtc;
            runner.ConnectionSessionCount = stats.ConnectionSessionCount;
            runner.TotalConnectedDurationSeconds = stats.TotalConnectedDurationSeconds;
        }

        return runners;
    }

    private async Task<IDictionary<Guid, Guid>> BuildHostedRunnerLookup(
        IEnumerable<Guid> runnerIds,
        CancellationToken cancellationToken)
    {
        var ids = runnerIds.ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, Guid>();

        return await dbContext.HostedRunners
            .AsNoTracking()
            .Where(x => x.RunnerId != null && ids.Contains(x.RunnerId.Value) && x.DeletedAt == null)
            .ToDictionaryAsync(x => x.RunnerId!.Value, x => x.Id, cancellationToken);
    }
}
