using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

public class FlowsService
{
    private readonly ApplicationDbContext dbContext;
    private readonly RunnerService runnerService;
    private readonly ScheduleService scheduleService;
    private readonly RecentActivityService recentActivityService;
    private readonly ProjectContextService projectContextService;
    private readonly Guid userId;
    private readonly Serilog.ILogger logger;

    public FlowsService
    (
        ApplicationDbContext dbContext,
        RunnerService runnerService,
        ScheduleService scheduleService,
        RecentActivityService recentActivityService,
        UserContextService userContextService,
        ProjectContextService projectContextService
    )
    {
        this.dbContext = dbContext;
        this.runnerService = runnerService;
        this.scheduleService = scheduleService;
        this.recentActivityService = recentActivityService;
        this.projectContextService = projectContextService;
        this.userId = userContextService.GetUserId();
        this.logger = Log.ForContext<FlowsService>();
    }

    public IEnumerable<FlowInfo> List()
    {
        // Get current project context (null if user has no project or hasn't selected one)
        var currentProjectId = projectContextService.GetCurrentProjectIdAsync().Result;
        
        var query = dbContext.Flows
            .Where(m => m.FlowUsers.Any(x => x.UserId == userId));
        
        // Filter by project only if user has selected a project
        if (currentProjectId.HasValue)
        {
            query = query.Where(m => m.ProjectId == currentProjectId.Value);
        }
        
        query = query.Include(m => m.FlowUsers);
        
        var flows =
            from m in query
            from s in dbContext.FlowExecutions
                .Where(s => m.Id == s.FlowId)
                .DefaultIfEmpty()
                .OrderByDescending(x => x.Started)
                .Take(1)    
            select new FlowInfo
            {
                Id = m.Id,
                Created = m.Created,
                Updated = m.Updated,
                Name = m.Name,
                Started = s != null ? s.Started : DateTime.MinValue,
                Finished = s != null ? s.Finished : DateTime.MinValue,
                Status = s != null ? s.Status : WorkflowStatus.None.ToString()
            };

        return flows;
    }

    public Flow Get(Guid id)
    {
        var entity = dbContext.Flows.SingleOrDefault(x => x.Id == id && x.FlowUsers.Any(x => x.UserId == userId));

        var flow = DeserializeFlow(entity.Body);

        return flow;
    }

    public async Task<Guid> Create(Flow flow)
    {
        flow.Id = Guid.NewGuid();
        flow.Created = DateTime.UtcNow;
        flow.Updated = DateTime.UtcNow;

        // Assign to current project if one is selected
        var currentProjectId = await projectContextService.GetCurrentProjectIdAsync();

        var flowEntity = new Entities.Flow
        {
            Id = flow.Id,
            Name = flow.Name,
            Body = JsonSerializer.Serialize(flow),
            Created = flow.Created,
            Updated = flow.Updated,
            ProjectId = currentProjectId
        };

        dbContext.Flows.Add(flowEntity);

        var flowUser = new Entities.FlowUser
        {
            FlowId = flowEntity.Id,
            UserId = userId
        };

        dbContext.FlowUsers.Add(flowUser);

        dbContext.SaveChanges();

        // Log activity
        await recentActivityService.LogFlowActivityAsync(ActivityType.FlowCreated, flow.Name, flow.Id);

        return flowEntity.Id;
    }

    public async Task Update(Guid id, Flow flow)
    {
        var entity = dbContext.Flows.SingleOrDefault(x => x.Id == id && x.FlowUsers.Any(x => x.UserId == userId));

        entity.Name = flow.Name;
        entity.Body = JsonSerializer.Serialize(flow);
        entity.Updated = DateTime.UtcNow;

        dbContext.SaveChanges();

        // Log activity
        await recentActivityService.LogFlowActivityAsync(ActivityType.FlowUpdated, flow.Name, id);
    }

    public async Task Remove(Guid id)
    {
        var flow = dbContext.Flows.SingleOrDefault(x => x.Id == id && x.FlowUsers.Any(x => x.UserId == userId));
        var flowName = flow.Name;

        dbContext.Flows.Remove(flow);
        dbContext.SaveChanges();

        // Log activity
        await recentActivityService.LogFlowActivityAsync(ActivityType.FlowDeleted, flowName, id);
    }

    public bool Exists(string name)
    {
        var exists = dbContext.Flows.Any(x => x.Name == name && x.FlowUsers.Any(x => x.UserId == userId));

        return exists;
    }

    /// <summary>
    /// Resolve flow name to flow ID for the current user (respects impersonation).
    /// </summary>
    /// <param name="flowName">Flow name to resolve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Flow ID if found, null otherwise</returns>
    public async Task<Guid?> ResolveFlowNameToId(string flowName, CancellationToken cancellationToken = default)
    {
        var flow = await dbContext.Flows
            .Include(f => f.FlowUsers)
            .FirstOrDefaultAsync(f =>
                f.Name == flowName &&
                f.FlowUsers.Any(u => u.UserId == userId),
                cancellationToken);

        return flow?.Id;
    }

    public async Task<IEnumerable<RunnerFlowResult>> Execute(Guid flowId, IEnumerable<Guid> runnerIds, CancellationToken cancellationToken)
    {
        var flow = dbContext.Flows.SingleOrDefault(x => x.Id == flowId && x.FlowUsers.Any(x => x.UserId == userId));
        
        var result = await runnerService.ExecuteFlow(flowId, runnerIds, cancellationToken);

        // Log activity
        if (flow != null)
        {
            await recentActivityService.LogFlowActivityAsync(ActivityType.FlowExecuted, flow.Name, flowId, cancellationToken);
        }

        return result;
    }

    public FlowSettings GetSettings(Guid id)
    {
        var flow = Get(id);

        var settings = new FlowSettings
        {
            Id = flow.Id,
            Name = flow.Name,
            Created = flow.Created,
            Updated = flow.Updated,
        };

        return settings;
    }

    private static Flow DeserializeFlow(string jsonFlow)
    {
        var flow = JsonSerializer.Deserialize<Flow>(jsonFlow);

        return flow;
    }
}
