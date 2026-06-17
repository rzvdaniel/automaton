using Automaton.Api.Data;
using Automaton.Api.Mapping;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public class AdminRunnersService
{
    private readonly ApplicationDbContext dbContext;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly AdminAuthorizationService adminAuthorizationService;

    public AdminRunnersService(
        ApplicationDbContext dbContext,
        ResourceAuthorizationService authorizationService,
        AdminAuthorizationService adminAuthorizationService)
    {
        this.dbContext = dbContext;
        this.authorizationService = authorizationService;
        this.adminAuthorizationService = adminAuthorizationService;
    }

    public async Task<IEnumerable<RunnerDetails>> ListOwnedByUserForAdmin(
        Guid userId,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        EnsureAdminAccess();

        var runnerEntities = await authorizationService.GetRunnersQueryByContext(projectId, userId)
            .ToListAsync(cancellationToken);

        var projectLookup = await BuildRunnerProjectLookupAsync(runnerEntities.Select(r => r.Id), projectId, userId, cancellationToken);
        var hostedRunnerLookup = await BuildHostedRunnerLookupAsync(runnerEntities.Select(r => r.Id), cancellationToken);
        var runners = RunnerMapper.ToRunnerDetailsList(runnerEntities, RunnerStatus.None, projectLookup, hostedRunnerLookup).ToList();
        var now = DateTime.UtcNow;

        foreach (var runner in runners)
        {
            runner.Status = RunnerPresenceEvaluator.DeriveStatus(runner.ConnectionId, runner.LastHeartbeatAtUtc, now);
        }

        return runners;
    }

    private async Task<Dictionary<Guid, (Guid ProjectId, string ProjectName)>> BuildRunnerProjectLookupAsync(
        IEnumerable<Guid> runnerIds,
        Guid? projectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var ids = runnerIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, (Guid ProjectId, string ProjectName)>();
        }

        var query = dbContext.ProjectRunners
            .Include(x => x.Project)
            .Where(x => ids.Contains(x.RunnerId));

        if (projectId.HasValue)
        {
            query = query.Where(x => x.ProjectId == projectId.Value);
        }
        else
        {
            query = query.Where(x => x.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive));
        }

        var links = await query.ToListAsync(cancellationToken);

        return links
            .GroupBy(x => x.RunnerId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var first = group.First();
                    return (first.ProjectId, first.Project.DisplayName);
                });
    }

    private async Task<IDictionary<Guid, Guid>> BuildHostedRunnerLookupAsync(IEnumerable<Guid> runnerIds, CancellationToken cancellationToken)
    {
        var ids = runnerIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        return await dbContext.HostedRunners
            .AsNoTracking()
            .Where(x => x.RunnerId != null && ids.Contains(x.RunnerId.Value) && x.DeletedAt == null)
            .ToDictionaryAsync(x => x.RunnerId!.Value, x => x.Id, cancellationToken);
    }

    private void EnsureAdminAccess() => adminAuthorizationService.EnsureAdmin();
}
