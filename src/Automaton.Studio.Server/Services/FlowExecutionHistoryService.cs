using AutoMapper;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

public class FlowExecutionHistoryService
{
    private readonly ApplicationDbContext dbContext;
    private readonly IMapper mapper;
    private readonly Guid userId;
    private readonly Serilog.ILogger logger;

    public FlowExecutionHistoryService
    (
        ApplicationDbContext dbContext,
        UserContextService userContextService,
        IMapper mapper
    )
    {
        this.dbContext = dbContext;
        this.mapper = mapper;
        this.userId = userContextService.GetUserId();
        this.logger = Serilog.Log.ForContext<FlowsService>();
    }

    public async Task<Models.FlowExecutionResult> GetAsync(Guid flowId, int startIndex, int pageSize, CancellationToken cancellationToken)
    {
        var executions = await GetListAsync(flowId, startIndex, pageSize, cancellationToken);
        var totalExecutions = await GetTotalAsync(flowId, cancellationToken);
        var result = new Models.FlowExecutionResult(executions, totalExecutions);

        return result;
    }

    public int Add(Models.FlowExecution executionModel)
    {
        var execution = new FlowExecution()
        {
            Id = executionModel.Id,
            FlowId = executionModel.FlowId,
            Status = executionModel.Status,
            Started = executionModel.Started,
            Finished = executionModel.Finished,
            Application = executionModel.Application
        };

        dbContext.FlowExecutions.Add(execution);

        var executionUser = new FlowExecutionUser
        {
            FlowExecutionId = execution.Id,
            UserId = userId
        };

        dbContext.FlowExecutionUsers.Add(executionUser);

        var result = dbContext.SaveChanges();

        return result;
    }

    public async Task Update(Guid id, Models.FlowExecution flowExecution, CancellationToken cancellationToken)
    {
        var entity = dbContext.FlowExecutions
            .Include(x => x.FlowExecutionUsers)
            .SingleOrDefault(x => x.Id == id &&
                x.FlowExecutionUsers.Any(x => x.UserId == userId));

        entity.Status = flowExecution.Status;
        entity.Finished = flowExecution.Finished;

        // Mark entity as modified
        dbContext.Entry(entity).State = EntityState.Modified;

        // Update runner entity
        dbContext.Update(entity);

        // Save changes
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IEnumerable<FlowExecution>> GetListAsync(Guid flowId, int startIndex, int pageSize, CancellationToken cancellationToken)
    {
        var execution = await dbContext.FlowExecutions
            .Where(x => x.FlowId == flowId && x.FlowExecutionUsers.Any(x => x.UserId == userId))
            .OrderByDescending(x => x.Started)
            .Skip(startIndex)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return execution;
    }

    private async Task<int> GetTotalAsync(Guid flowId, CancellationToken cancellationToken)
    {
        var total = await dbContext.FlowExecutions.CountAsync(x => x.FlowId == flowId && x.FlowExecutionUsers.Any(x => x.UserId == userId), cancellationToken);

        return total;
    }
}
