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

        // Add execution logs as single JSON entry
        if (executionModel.Logs != null && executionModel.Logs.Any())
        {
            var logEntries = executionModel.Logs.Select(log => new Entities.LogEntry
            {
                Timestamp = log.Timestamp,
                Level = log.Level,
                Message = log.Message,
                StepId = log.StepId,
                StepName = log.StepName,
                Exception = log.Exception
            }).ToList();

            var executionLog = new FlowExecutionLog
            {
                Id = Guid.NewGuid(),
                FlowExecutionId = execution.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            executionLog.SetLogEntries(logEntries);
            dbContext.FlowExecutionLogs.Add(executionLog);
        }

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

    private async Task<IEnumerable<Entities.FlowExecution>> GetListAsync(Guid flowId, int startIndex, int pageSize, CancellationToken cancellationToken)
    {
        var executions = await dbContext.FlowExecutions
            .Include(x => x.FlowExecutionUsers)
            .Where(x => x.FlowId == flowId && x.FlowExecutionUsers.Any(u => u.UserId == userId))
            .OrderByDescending(x => x.Started)
            .Skip(startIndex)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return executions;
    }

    private async Task<int> GetTotalAsync(Guid flowId, CancellationToken cancellationToken)
    {
        return await dbContext.FlowExecutions
            .Where(x => x.FlowId == flowId && x.FlowExecutionUsers.Any(u => u.UserId == userId))
            .CountAsync(cancellationToken);
    }

    private static Models.FlowExecution MapToModel(Entities.FlowExecution entity)
    {
        return new Models.FlowExecution
        {
            Id = entity.Id,
            FlowId = entity.FlowId,
            Started = entity.Started,
            Finished = entity.Finished,
            Status = entity.Status,
            Application = entity.Application
        };
    }
}
