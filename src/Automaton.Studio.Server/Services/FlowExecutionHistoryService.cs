using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

public class FlowExecutionHistoryService
{
    private readonly ApplicationDbContext dbContext;
    private readonly Guid userId;
    private readonly Serilog.ILogger logger;

    public FlowExecutionHistoryService
    (
        ApplicationDbContext dbContext,
        UserContextService userContextService
    )
    {
        this.dbContext = dbContext;
        this.userId = userContextService.GetUserId();
        this.logger = Serilog.Log.ForContext<FlowExecutionHistoryService>();
    }

    public async Task<Models.FlowExecutionResult> GetAsync(Guid flowId, int startIndex, int pageSize, CancellationToken cancellationToken)
    {
        var executions = await GetListAsync(flowId, startIndex, pageSize, cancellationToken);
        var totalExecutions = await GetTotalAsync(flowId, cancellationToken);
        var result = new Models.FlowExecutionResult(executions, totalExecutions);

        return result;
    }

    public async Task<IEnumerable<Models.FlowExecution>> GetRecentExecutionsAsync(int count, CancellationToken cancellationToken = default)
    {
        try
        {
            var executions = await dbContext.FlowExecutions
                .Include(x => x.FlowExecutionUsers)
                .Where(x => x.FlowExecutionUsers.Any(u => u.UserId == userId))
                .OrderByDescending(x => x.Started)
                .Take(count)
                .ToListAsync(cancellationToken);

            return executions.Select(MapToModel);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to get recent executions for user {UserId}", userId);
            return new List<Models.FlowExecution>();
        }
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
        var total = await dbContext.FlowExecutions
            .Where(x => x.FlowId == flowId && x.FlowExecutionUsers.Any(x => x.UserId == userId))
            .CountAsync(cancellationToken);

        return total;
    }

    private static Models.FlowExecution MapToModel(Entities.FlowExecution entity)
    {
        return new Models.FlowExecution
        {
            Id = entity.Id,
            FlowId = entity.FlowId,
            Status = entity.Status,
            Started = entity.Started,
            Finished = entity.Finished,
            Application = entity.Application
        };
    }
}
