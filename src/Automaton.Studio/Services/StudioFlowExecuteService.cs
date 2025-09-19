using AutoMapper;
using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Studio.Domain;

namespace Automaton.Studio.Services;

public class StudioFlowExecuteService : BaseFlowExecuteService<StudioFlow>
{
    private readonly IMapper mapper;
    private readonly IMediator mediator;
    private readonly ConfigurationService configurationService;
    private readonly FlowExecutionsService flowExecutionsService;
    
    public StudioFlowExecuteService(
        StudioFlowConvertService studioFlowConvertService, 
        FlowExecutionsService flowExecutionsService,
        ConfigurationService configurationService, 
        IMediator mediator, 
        IMapper mapper)
        : base(studioFlowConvertService, new MediatRExecutionNotificationService(mediator), configurationService.ApplicationName)
    {
        this.mapper = mapper;
        this.mediator = mediator;
        this.configurationService = configurationService;
        this.flowExecutionsService = flowExecutionsService;
    }

    /// <summary>
    /// Studio-specific post-execution processing - saves execution results to database
    /// </summary>
    protected override async Task OnExecutionCompleted(WorkflowExecution workflowExecution)
    {
        await SaveWorkflowExecution(workflowExecution);
        logger.Information("Successfully saved workflow execution {ExecutionId}", workflowExecution.Id);
    }

    private async Task SaveWorkflowExecution(WorkflowExecution workflowExecution)
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
    }
}
