using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Core.Scripting.Engines;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Hubs;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Mapping;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

public class RunnerService
{
    private readonly ApplicationDbContext dbContext;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly UserContextService userContextService;  // ✅ Store service, not userId
    private readonly ResourceAuthorizationService authorizationService;
    private readonly Serilog.ILogger logger;

    // ✅ Helper property to get current userId (respects impersonation)
    private Guid UserId => userContextService.GetUserId();

    public RunnerService(ApplicationDbContext dbContext,
        UserContextService userContextService,
        IHubContext<AutomatonHub> automatonHub,
        ResourceAuthorizationService authorizationService)
    {
        this.dbContext = dbContext;
        this.automatonHub = automatonHub;
        this.userContextService = userContextService;  // ✅ Store service reference
        this.authorizationService = authorizationService;
        logger = Serilog.Log.ForContext<RunnerService>();
    }

    public async Task<IEnumerable<RunnerDetails>> List(CancellationToken cancellationToken)
    {
        return await List(null, cancellationToken);
    }

    public async Task<IEnumerable<RunnerDetails>> List(Guid? projectId, CancellationToken cancellationToken)
    {
        // Use authorization service to get runners filtered by project context
        var runnerQuery = authorizationService.GetRunnersQueryByContext(projectId);
        var runnerEntities = await runnerQuery.ToListAsync(cancellationToken);

        var projectLookup = await BuildRunnerProjectLookup(
            runnerEntities.Select(r => r.Id),
            projectId,
            cancellationToken);

        return await GetRunnersWithUpdatedStatus(runnerEntities, projectLookup, cancellationToken);
    }

    public Runner Get(Guid id)
    {
        // Use authorization service query to check access (personal + project-based)
        var runner = authorizationService.GetAccessibleRunnersQuery()
            .SingleOrDefault(x => x.Id == id);

        // Because we update Runner's ConnectionId on the fly,
        // when retrieving data we get the cached version of it
        // with previous ConnectionId. There is no need to do the
        // same thing with other entities if they aren't updated
        // in the same way as the Runner entity.

        // Here are some ideas to fix the issue:
        // https://stackoverflow.com/a/51290890/778863
        // http://codethug.com/2016/02/19/Entity-Framework-Cache-Busting/

        // Solution 1. Reload the entity 
        dbContext.Entry(runner).Reload();

        // Solution 2. Detach the entity to remove it from context’s cache.
        // dbContext.Entry(entity).State = EntityState.Detached;
        // entity = dbContext.Runners.Find(id);

        return runner;
    }

    public int Add(RunnerDetails runnerModel)
    {
        logger.Information("=== ADD RUNNER DEBUG ===");
        logger.Information("Runner model: ID={Id}, Name={Name}", runnerModel.Id, runnerModel.Name);
        logger.Information("UserId from context: {UserId}", UserId);
        logger.Information("UserId is empty: {IsEmpty}", UserId == Guid.Empty);

        if (UserId == Guid.Empty)
        {
            logger.Error("Cannot add runner: userId is empty/null. User context not properly set from authentication");
            logger.Error("This usually means API key authentication claims are not being properly converted to user context");
            throw new InvalidOperationException("User context not available. Authentication may not have been properly processed. UserId is empty.");
        }

        var runner = new Runner()
        {
            Id = runnerModel.Id,
            Name = runnerModel.Name
        };

        dbContext.Runners.Add(runner);

        var runnerUser = new RunnerUser
        {
            RunnerId = runner.Id,
            UserId = UserId
        };

        dbContext.RunnerUsers.Add(runnerUser);

        var result = dbContext.SaveChanges();

        logger.Information("Runner created successfully: ID={RunnerId}, Name={Name}, UserId={UserId}, Changes={Changes}",
            runner.Id, runner.Name, UserId, result);
        logger.Information("=== END ADD RUNNER DEBUG ===");

        return result;
    }

    public async Task UpdateConnection(Guid runnerId, string connectionId, CancellationToken cancellationToken)
    {
        // Check if user can update this runner (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateRunnerAsync(runnerId, cancellationToken))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update runner {runnerId}");
        }

        var runner = dbContext.Runners
            .Include(x => x.RunnerUsers)
            .SingleOrDefault(x => x.Id == runnerId);

        if (runner == null)
        {
            throw new InvalidOperationException($"Runner {runnerId} not found");
        }

        runner.ConnectionId = connectionId;

        // Mark entity as modified
        dbContext.Entry(runner).State = EntityState.Modified;

        // Update runner entity
        dbContext.Update(runner);

        // Save changes
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Update(Guid id, RunnerDetails runner, CancellationToken cancellationToken)
    {
        // Check if user can update this runner (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateRunnerAsync(id, cancellationToken))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update runner {id}");
        }

        var entity = dbContext.Runners
            .Include(x => x.RunnerUsers)
            .SingleOrDefault(x => x.Id == id);

        if (entity == null)
        {
            throw new InvalidOperationException($"Runner {id} not found");
        }

        entity.Name = runner.Name;

        // Mark entity as modified
        dbContext.Entry(entity).State = EntityState.Modified;

        // Update runner entity
        dbContext.Update(entity);

        // Save changes
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public bool Exists(string name)
    {
        var exists = dbContext.Runners.Any(x =>
            x.Name.ToLower() == name.ToLower() &&
            x.RunnerUsers.Any(x => x.UserId == UserId));

        return exists;
    }

    public bool ExistsById(Guid id)
    {
        var exists = dbContext.Runners.Any(x =>
         x.Id == id &&
            x.RunnerUsers.Any(x => x.UserId == UserId));

        return exists;
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken)
    {
        // Check if user can delete this runner (must be personal owner AND not part of any project)
        if (!await authorizationService.CanDeleteRunnerAsync(id, cancellationToken))
        {
            // Check if it's because the runner is part of a project
            var isPartOfProject = await dbContext.ProjectRunners
                .AnyAsync(pr => pr.RunnerId == id, cancellationToken);

            if (isPartOfProject)
            {
                throw new InvalidOperationException($"Cannot delete runner {id} because it is part of one or more projects. Remove it from all projects first.");
            }

            throw new UnauthorizedAccessException($"You do not have permission to delete runner {id}");
        }

        var entity = dbContext.Runners
            .Include(x => x.RunnerUsers)
            .SingleOrDefault(x => x.Id == id);

        if (entity != null)
        {
            dbContext.Runners.Remove(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<RunnerFlowResult>> ExecuteFlow(Guid flowId, IEnumerable<Guid> runnerIds, CancellationToken cancellationToken)
    {
        var results = new List<RunnerFlowResult>();

        foreach (var runnerId in runnerIds)
        {
            RunnerFlowResult result;
            var runner = Get(runnerId);
            var client = automatonHub.Clients.Client(runner.ConnectionId);

            try
            {
                var response = await client.InvokeAsync<WorkflowExecution>(AutomatonHubMethods.RunWorkflow, flowId, cancellationToken);

                result = GetSuccessfulFlowResult(flowId: flowId, runnerId: runnerId, response);
            }
            catch (Exception ex)
            {
                logger.ForContext("FlowId", flowId)
                      .ForContext("RunnerId", runnerId)
                      .Error(ex, "An error happened when executing flow on runner");

                result = GetInvalidFlowResult(flowId: flowId, runnerId: runnerId);
            }

            results.Add(result);
        }

        return results;
    }

    public async Task<IEnumerable<RunnerFlowResult>> ExecuteFlow(Guid flowId, IEnumerable<Guid> runnerIds, Guid userId, CancellationToken cancellationToken)
    {
        var results = new List<RunnerFlowResult>();

        foreach (var runnerId in runnerIds)
        {
            RunnerFlowResult result;
            var runner = Get(runnerId);
            var client = automatonHub.Clients.Client(runner.ConnectionId);

            try
            {
                var response = await client.InvokeAsync<WorkflowExecution>(AutomatonHubMethods.RunWorkflow, flowId, cancellationToken);

                result = GetSuccessfulFlowResult(flowId: flowId, runnerId: runnerId, response);
            }
            catch (Exception ex)
            {
                logger.ForContext("FlowId", flowId)
                      .ForContext("RunnerId", runnerId)
                      .Error(ex, "An error happened when executing flow on runner");

                result = GetInvalidFlowResult(flowId: flowId, runnerId: runnerId);
            }

            results.Add(result);
        }

        return results;
    }

    private async Task<Dictionary<Guid, (Guid ProjectId, string ProjectName)>> BuildRunnerProjectLookup(
        IEnumerable<Guid> runnerIds,
        Guid? projectId,
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
            var userId = UserId;
            query = query.Where(x => x.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive));
        }

        var links = await query.ToListAsync(cancellationToken);

        return links
            .GroupBy(x => x.RunnerId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var first = g.First();
                    return (first.ProjectId, first.Project.DisplayName);
                });
    }

    private async Task<IEnumerable<RunnerDetails>> GetRunnersWithUpdatedStatus(
        IEnumerable<Runner> runnerEntities,
        IDictionary<Guid, (Guid ProjectId, string ProjectName)> projectLookup,
        CancellationToken cancellationToken)
    {
        var runners = RunnerMapper
            .ToRunnerDetailsList(runnerEntities, RunnerStatus.None, projectLookup)
            .ToList();

        foreach (var runner in runners)
        {
            try
            {
                var client = automatonHub.Clients.Client(runner.ConnectionId);

                var response = await client.InvokeAsync<string>(AutomatonHubMethods.Ping, cancellationToken);

                runner.Status = GetRunnerStatus(response);
            }
            catch (Exception ex)
            {
                runner.Status = RunnerStatus.Offline;

                logger.ForContext("RunnerName", runner.Name)
                      .ForContext("RunnerId", runner.Id)
                      .Error(ex, "Could not get runner status");
            }
        }

        return runners;
    }

    private static RunnerStatus GetRunnerStatus(string response)
    {
        return response.Equals("Pong", StringComparison.OrdinalIgnoreCase) ?
            RunnerStatus.Online :
            RunnerStatus.Offline;
    }

    private RunnerFlowResult GetSuccessfulFlowResult(Guid flowId, Guid runnerId, WorkflowExecution flowExecution)
    {
        return new RunnerFlowResult
        {
            FlowId = flowId,
            RunnerId = runnerId,
            Started = flowExecution.Started,
            Finished = flowExecution.Finished,
            Status = flowExecution.Status,

            // ✅ Include OutputVariables from flow execution
            OutputVariables = flowExecution.OutputVariables
                .ToDictionary(k => k.Key, v => v.Value.Value),

            // ✅ Include execution logs for debugging
            Logs = flowExecution.Logs
                .Select(log => $"[{log.Timestamp:HH:mm:ss}] [{log.Level}] {log.Message}")
                .ToList()
        };
    }

    private RunnerFlowResult GetInvalidFlowResult(Guid flowId, Guid runnerId)
    {
        return new RunnerFlowResult
        {
            FlowId = flowId,
            RunnerId = runnerId,
            Started = DateTime.MinValue,
            Finished = DateTime.MinValue,
            Status = WorkflowStatus.None
        };
    }

    /// <summary>
    /// Get script engine settings for a specific runner
    /// </summary>
    public async Task<RunnerScriptEngineSettings?> GetScriptEngineSettings(Guid id, CancellationToken cancellationToken)
    {
        // Check if user can access this runner (personal owner OR project member)
        if (!await authorizationService.CanAccessRunnerAsync(id, cancellationToken))
        {
            return null;
        }

        var runner = await dbContext.Runners
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (runner == null || string.IsNullOrWhiteSpace(runner.ScriptEngineSettings))
        {
            return null;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<RunnerScriptEngineSettings>(
                runner.ScriptEngineSettings,
               new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                   );

            return settings;
        }
        catch (JsonException ex)
        {
            logger.Warning(ex, "Failed to deserialize script engine settings for runner {RunnerId}", id);
            return null;
        }
    }

    /// <summary>
    /// Update script engine settings for a specific runner
    /// </summary>
    public async Task UpdateScriptEngineSettings(Guid id, string? settingsJson, CancellationToken cancellationToken)
    {
        // Check if user can update this runner (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateRunnerAsync(id, cancellationToken))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update runner {id}");
        }

        var runner = await dbContext.Runners
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (runner == null)
        {
            throw new InvalidOperationException($"Runner {id} not found");
        }

        runner.ScriptEngineSettings = settingsJson;

        dbContext.Entry(runner).State = EntityState.Modified;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Updated script engine settings for runner {RunnerId}", id);
    }

    public async Task<RunnerDetails?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        // Use authorization service query to check access (personal + project-based)
        var runner = await authorizationService.GetAccessibleRunnersQuery()
            .FirstOrDefaultAsync(x => x.Name.ToLower() == name.ToLower(), cancellationToken);

        if (runner == null)
        {
            return null;
        }

        var projectLookup = await BuildRunnerProjectLookup(
            new[] { runner.Id },
            null,
            cancellationToken);

        if (projectLookup.TryGetValue(runner.Id, out var link))
        {
            return RunnerMapper.ToRunnerDetails(runner, RunnerStatus.None, link.ProjectId, link.ProjectName);
        }

        return RunnerMapper.ToRunnerDetails(runner);
    }
}
