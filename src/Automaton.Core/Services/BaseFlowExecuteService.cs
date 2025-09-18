using Automaton.Core.Interfaces;
using Automaton.Core.Logs;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Serilog;
using Serilog.Context;

namespace Automaton.Core.Services
{
    public abstract class BaseFlowExecuteService : IFlowExecuteService
    {
        protected readonly ILogger logger;
        protected readonly FlowConvertService flowConvertService;
        protected readonly IExecutionNotificationService notificationService;
        protected readonly string applicationName;

        protected BaseFlowExecuteService(
            FlowConvertService flowConvertService,
            IExecutionNotificationService notificationService,
            string applicationName)
        {
            this.flowConvertService = flowConvertService;
            this.notificationService = notificationService;
            this.applicationName = applicationName;
            this.logger = Log.ForContext(GetType());
        }

        public virtual async Task<WorkflowExecution> Execute(Flow flow, CancellationToken cancellationToken = default)
        {
            var workflow = flowConvertService.ConvertFlow(flow);
            return await Execute(workflow, 0, cancellationToken);
        }

        public virtual async Task<WorkflowExecution> Execute(Workflow workflow, int executeDelay = 0, CancellationToken cancellationToken = default)
        {
            using var workflowExecution = new WorkflowExecution(workflow.Id, applicationName);
            var definition = workflow.GetStartupDefinition();
            var step = definition.GetFirstStep();

            // Set up logging context
            LogContext.PushProperty(LogContextProperties.WorkflowId, workflow.Id);
            LogContext.PushProperty(LogContextProperties.WorkflowExecutionId, workflowExecution.Id);
            LogContext.PushProperty(LogContextProperties.WorkflowName, workflow.Name);

            // Handle variable updates
            workflow.SetWorkflowVariable += async (sender, e) =>
            {
                await notificationService.SendVariableUpdate(e.Variable);
            };

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

            // Allow derived classes to perform additional actions after execution
            await OnExecutionCompleted(workflowExecution);

            return workflowExecution;
        }

        /// <summary>
        /// Unified logging method that handles persistence, Serilog, and notifications
        /// </summary>
        protected virtual async Task AddLogWithNotification(
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

        protected virtual void LogToSerilog(string level, string message, Exception exception = null)
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

        /// <summary>
        /// Override this method in derived classes to perform additional actions after execution
        /// </summary>
        protected virtual async Task OnExecutionCompleted(WorkflowExecution workflowExecution)
        {
            await Task.CompletedTask;
        }
    }
}