using Automaton.Core.Interfaces;
using Automaton.Core.Models;
using Automaton.Studio.Services;
using Serilog;

namespace Automaton.Studio.Services;

/// <summary>
/// Studio persistence service that saves execution results using FlowExecutionsService
/// </summary>
public class StudioWorkflowExecutionPersistenceService : IWorkflowExecutionPersistenceService
{
    private readonly FlowExecutionsService flowExecutionsService;
    private readonly ILogger logger;

    public StudioWorkflowExecutionPersistenceService(FlowExecutionsService flowExecutionsService)
    {
        this.flowExecutionsService = flowExecutionsService;
        this.logger = Log.ForContext<StudioWorkflowExecutionPersistenceService>();
    }

    public async Task SaveWorkflowExecutionAsync(WorkflowExecution workflowExecution)
    {
        var flowExecution = new Models.FlowExecution
        {
            Id = workflowExecution.Id,
            FlowId = workflowExecution.FlowId,
            Started = workflowExecution.Started,
            Finished = workflowExecution.Finished,
            Status = workflowExecution.Status,
            Application = workflowExecution.Application,
            Logs = workflowExecution.Logs?.Select(log => new Models.FlowExecutionLogEntry
            {
                Timestamp = log.Timestamp,
                Level = log.Level,
                Message = log.Message,
                StepId = log.StepId,
                StepName = log.StepName,
                Exception = log.Exception
            }).ToList() ?? new List<Models.FlowExecutionLogEntry>()
        };

        await flowExecutionsService.Add(flowExecution);
        logger.Information("Successfully saved workflow execution {ExecutionId} via FlowExecutionsService", workflowExecution.Id);
    }
}