using Automaton.Core.Logs;
using Automaton.Core.Models;
using MediatR;
using Serilog;
using Serilog.Context;

namespace Automaton.Core.Services;

public class FlowExecuteService
{
    private readonly IMediator mediator;
    private readonly ILogger logger;
    private readonly FlowConvertService flowConvertService;

    public FlowExecuteService(FlowConvertService flowConvertService, IMediator mediator)
    {
        this.mediator = mediator;
        this.flowConvertService = flowConvertService;
        logger = Log.ForContext<FlowExecuteService>();
    }

    public async Task<WorkflowExecution> Execute(Flow flow, CancellationToken cancellationToken = default)
    {
        var workflow = flowConvertService.ConvertFlow(flow);

        var result = await Execute(workflow, cancellationToken);

        return result;
    }

    public async Task<WorkflowExecution> Execute(Workflow workflow, CancellationToken cancellationToken = default)
    {
        // TODO! Add parent workflow as executing application name instead of an empty string?
        using var workflowExecution = new WorkflowExecution(workflow.Id, string.Empty);

        var definition = workflow.GetStartupDefinition();
        var step = definition.GetFirstStep();

        LogContext.PushProperty(LogContextProperties.WorkflowId, workflow.Id);
        LogContext.PushProperty(LogContextProperties.WorkflowExecutionId, workflowExecution.Id);
        LogContext.PushProperty(LogContextProperties.WorkflowName, workflow.Name);

        var startMessage = $"Start workflow: {workflow.Name}";
        logger.Information(startMessage);
        workflowExecution.AddLog("Information", startMessage);

        while (step != null)
        {
            var context = new StepExecutionContext
            {
                Workflow = workflow,
                Definition = definition,
                Step = step,
                CancellationToken = cancellationToken
            };

            try
            {
                var stepStartMessage = $"Executing step: {step.Name} (ID: {step.Id})";
                logger.Information(stepStartMessage);
                workflowExecution.AddLog("Information", stepStartMessage, step.Id, step.Name);

                await step.ExecuteAsync(context);

                var stepCompleteMessage = $"Completed step: {step.Name}";
                logger.Information(stepCompleteMessage);
                workflowExecution.AddLog("Information", stepCompleteMessage, step.Id, step.Name);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Step: {step.Id} encountered an error. Message: {ex.Message}";
                logger.Error(ex, errorMessage);
                workflowExecution.AddLog("Error", errorMessage, step.Id, step.Name, ex);
                workflowExecution.HasErrors();
            }

            step = step.GetNextStep();
        }

        var endMessage = $"End workflow: {workflow.Name}";
        logger.Information(endMessage);
        workflowExecution.AddLog("Information", endMessage);

        return workflowExecution;
    }
}
