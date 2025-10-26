using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Studio.Domain;

namespace Automaton.Studio.Services;

public class StudioFlowExecuteService : BaseFlowExecuteService<StudioFlow>
{
    private readonly IMediator mediator;
    private readonly ConfigurationService configurationService;
    
    public StudioFlowExecuteService(
        StudioFlowConvertService studioFlowConvertService, 
        FlowExecutionsService flowExecutionsService,
        ConfigurationService configurationService, 
        IMediator mediator)
        : base(studioFlowConvertService, 
               new MediatRExecutionNotificationService(mediator), 
               configurationService.ApplicationName,
               new StudioWorkflowExecutionPersistenceService(flowExecutionsService))
    {
        this.mediator = mediator;
        this.configurationService = configurationService;
    }

    /// <summary>
    /// Studio-specific post-execution processing override
    /// Note: Execution persistence is now handled by the base class automatically
    /// This method is available for any additional studio-specific processing if needed in the future
    /// </summary>
    protected override async Task OnExecutionCompleted(WorkflowExecution workflowExecution)
    {
        // Execution is already saved by the base class via StudioWorkflowExecutionPersistenceService
        // This method is left available for future studio-specific post-processing needs
        await base.OnExecutionCompleted(workflowExecution);
    }
}
