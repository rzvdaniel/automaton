using AutoMapper;
using Automaton.Core.Logs;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Automaton.Core.Services;
using Automaton.Studio.Domain;
using Automaton.Studio.Events;
using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

public class StudioFlowExecuteService
{
    private readonly IMapper mapper;
    private readonly IMediator mediator;
    private readonly ILogger logger;
    private readonly ConfigurationService configurationService;
    private readonly StudioFlowConvertService studioFlowConvertService;
    private readonly FlowExecutionsService flowExecutionsService;
    private readonly MediatRExecutionNotificationService notificationService;
    
    public StudioFlowExecuteService
    (
        StudioFlowConvertService studioFlowConvertService, 
        FlowExecutionsService flowExecutionsService,
        ConfigurationService configurationService, 
        IMediator mediator, 
        IMapper mapper
    )
    {
        this.mapper = mapper;
        this.mediator = mediator;
        this.configurationService = configurationService;
        this.studioFlowConvertService = studioFlowConvertService;
        this.flowExecutionsService = flowExecutionsService;
        this.notificationService = new MediatRExecutionNotificationService(mediator);
        this.logger = Log.ForContext<StudioFlowExecuteService>();
    }

    /// <summary>
    /// Studio-specific execute method that accepts StudioFlow
    /// </summary>
    public async Task<WorkflowExecution> Execute(StudioFlow flow, int executeDelay = 0, CancellationToken cancellationToken = default)
    {
        var workflow = studioFlowConvertService.ConvertFlow(flow);

        // Handle variable updates
        workflow.SetWorkflowVariable += async (sender, e) =>
        {
            await notificationService.SendVariableUpdate(e.Variable);
        };

        WorkflowExecution workflowExecution = null;
        
        try
        {
            workflowExecution = await ExecuteWorkflow(workflow, executeDelay, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Workflow execution failed for workflow {WorkflowId}", workflow.Id);
            
            // Create error execution if workflow execution failed completely
            if (workflowExecution == null)
            {
                workflowExecution = new WorkflowExecution(workflow.Id, configurationService.ApplicationName)
                {
                    Started = DateTime.UtcNow,
                    Finished = DateTime.UtcNow,
                    Status = WorkflowStatus.Error
                };
                await AddLogWithNotification(workflowExecution, "Error", $"Workflow execution failed: {ex.Message}", exception: ex, cancellationToken: cancellationToken);
            }
        }

        // Always attempt to save execution results, but don't let save failures break the flow
        try 
        {
            await SaveWorkflowExecution(workflowExecution);
            logger.Information("Successfully saved workflow execution {ExecutionId}", workflowExecution.Id);
        }
        catch (Exception saveEx)
        {
            logger.Error(saveEx, "Failed to save workflow execution {ExecutionId}. Execution completed but results not persisted.", workflowExecution.Id);
            
            // Don't throw - the workflow executed, we just couldn't save the results
            // This prevents the UI from hanging and allows user to see the execution completed
        }

        return workflowExecution;
    }

    private async Task<WorkflowExecution> ExecuteWorkflow(Workflow workflow, int executeDelay = 0, CancellationToken cancellationToken = default)
    {
        using var workflowExecution = new WorkflowExecution(workflow.Id, configurationService.ApplicationName);
        var definition = workflow.GetStartupDefinition();
        var step = definition.GetFirstStep();

        // Set up logging context
        LogContext.PushProperty(LogContextProperties.WorkflowId, workflow.Id);
        LogContext.PushProperty(LogContextProperties.WorkflowExecutionId, workflowExecution.Id);
        LogContext.PushProperty(LogContextProperties.WorkflowName, workflow.Name);

        var startMessage = $"Start workflow: {workflow.Name}";
        await AddLogWithNotification(workflowExecution, "Information", startMessage, cancellationToken: cancellationToken);

        while (step != null && !cancellationToken.IsCancellationRequested)
        {
            var context = new StepExecutionContext
            {
                Workflow = workflow,
                Definition = definition,
                Step = step,
                CancellationToken = cancellationToken,
            };

            try
            {
                if (executeDelay > 0)
                {
                    await Task.Delay(executeDelay, cancellationToken);
                }

                // Notify step execution start
                await notificationService.SendStepExecution(step.Id);

                var stepStartMessage = $"Executing step: {step.Name} (ID: {step.Id})";
                await AddLogWithNotification(workflowExecution, "Information", stepStartMessage, step.Id, step.Name, cancellationToken: cancellationToken);

                await step.ExecuteAsync(context);

                var stepCompleteMessage = $"Completed step: {step.Name}";
                await AddLogWithNotification(workflowExecution, "Information", stepCompleteMessage, step.Id, step.Name, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                var cancelMessage = "Workflow execution was canceled";
                await AddLogWithNotification(workflowExecution, "Warning", cancelMessage, cancellationToken: cancellationToken);
                break;
            }
            catch (Exception ex)
            {
                var errorMessage = $"Step {step.Name} with Id {step.Id} encountered an error. Message: {ex.Message}";
                await AddLogWithNotification(workflowExecution, "Error", errorMessage, step.Id, step.Name, ex, cancellationToken);
                workflowExecution.HasErrors();
            }

            step = step.GetNextStep();
        }

        var endMessage = $"End workflow: {workflow.Name}";
        await AddLogWithNotification(workflowExecution, "Information", endMessage, cancellationToken: cancellationToken);

        return workflowExecution;
    }

    /// <summary>
    /// Unified logging method that handles persistence, Serilog, and notifications
    /// </summary>
    private async Task AddLogWithNotification(
        WorkflowExecution workflowExecution,
        string level,
        string message,
        string stepId = null,
        string stepName = null,
        Exception exception = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Add to workflow execution for persistence
        workflowExecution.AddLog(level, message, stepId, stepName, exception);

        // 2. Log to Serilog for server-side logging
        LogToSerilog(level, message, exception);

        // 3. Send real-time notification
        try
        {
            await notificationService.SendLogUpdate(
                workflowExecution.FlowId,
                workflowExecution.Id,
                level,
                message,
                stepId,
                stepName,
                exception?.ToString());
        }
        catch (Exception notificationEx)
        {
            logger.Warning(notificationEx, "Failed to send log update notification for execution {ExecutionId}", workflowExecution.Id);
        }
    }

    private void LogToSerilog(string level, string message, Exception exception = null)
    {
        switch (level.ToLowerInvariant())
        {
            case "error":
                if (exception != null)
                    logger.Error(exception, message);
                else
                    logger.Error(message);
                break;
            case "warning":
                logger.Warning(message);
                break;
            case "debug":
                logger.Debug(message);
                break;
            default:
                logger.Information(message);
                break;
        }
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
