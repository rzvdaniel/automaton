using Automaton.Core.Interfaces;
using Automaton.Core.Logs;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Serilog;
using Serilog.Context;

namespace Automaton.Core.Services
{
    /// <summary>
    /// Generic base class for flow execution services that can work with different flow model types
    /// </summary>
    /// <typeparam name="TFlow">The flow model type this service can execute</typeparam>
    public abstract class BaseFlowExecuteService<TFlow> : IFlowExecuteService<TFlow> where TFlow : class
    {
        protected readonly ILogger logger;
        protected readonly IFlowConvertService<TFlow> flowConvertService;
        protected readonly IExecutionNotificationService notificationService;
        protected readonly IWorkflowExecutionPersistenceService persistenceService;
        protected readonly string applicationName;

        protected BaseFlowExecuteService(
            IFlowConvertService<TFlow> flowConvertService,
            IExecutionNotificationService notificationService,
            string applicationName,
            IWorkflowExecutionPersistenceService persistenceService = null)
        {
            this.flowConvertService = flowConvertService;
            this.notificationService = notificationService;
            this.applicationName = applicationName;
            this.persistenceService = persistenceService ?? new NullWorkflowExecutionPersistenceService();
            this.logger = Log.ForContext(GetType());
        }

        /// <summary>
        /// Executes a domain-specific flow by converting it to a workflow first
        /// </summary>
        public virtual async Task<WorkflowExecution> Execute(TFlow flow, int executeDelay = 0, CancellationToken cancellationToken = default)
        {
            var workflow = flowConvertService.ConvertFlow(flow);

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
                    workflowExecution = new WorkflowExecution(workflow.Id, applicationName)
                    {
                        Started = DateTime.UtcNow,
                        Finished = DateTime.UtcNow,
                        Status = WorkflowStatus.Error
                    };
                    await AddLogWithNotification(workflowExecution, "Error", $"Workflow execution failed: {ex.Message}", exception: ex, cancellationToken: cancellationToken);
                }
            }

            // Save execution results using the common persistence mechanism
            try
            {
                await SaveWorkflowExecution(workflowExecution);
                logger.Information("Successfully saved workflow execution {ExecutionId}", workflowExecution.Id);
            }
            catch (Exception saveEx)
            {
                logger.Error(saveEx, "Failed to save workflow execution {ExecutionId}. Execution completed but results not persisted.", workflowExecution.Id);
                // Don't throw - the workflow executed successfully, we just couldn't save the results
            }

            // Allow derived classes to perform additional actions after execution
            try
            {
                await OnExecutionCompleted(workflowExecution);
                logger.Information("Successfully completed post-execution processing for execution {ExecutionId}", workflowExecution.Id);
            }
            catch (Exception postEx)
            {
                logger.Error(postEx, "Post-execution processing failed for execution {ExecutionId}. Execution completed but additional processing failed.", workflowExecution.Id);
                // Don't throw - the workflow executed successfully, we just couldn't do post-processing
            }

            return workflowExecution;
        }

        /// <summary>
        /// IFlowExecuteService implementation - works with Workflow directly
        /// </summary>
        public virtual async Task<WorkflowExecution> Execute(Workflow workflow, int executeDelay = 0, CancellationToken cancellationToken = default)
        {
            return await ExecuteWorkflow(workflow, executeDelay, cancellationToken);
        }

        /// <summary>
        /// IFlowExecuteService implementation - works with Flow (needs to be implemented by derived classes if supported)
        /// For backward compatibility - should be overridden to avoid ambiguous calls
        /// </summary>
        public virtual async Task<WorkflowExecution> Execute(Flow flow, CancellationToken cancellationToken = default)
        {
            // Base implementation throws - derived classes can override if they support Flow conversion
            throw new NotSupportedException($"{GetType().Name} does not support direct Flow execution. Use the domain-specific Execute method instead.");
        }

        /// <summary>
        /// Core workflow execution logic
        /// </summary>
        protected virtual async Task<WorkflowExecution> ExecuteWorkflow(Workflow workflow, int executeDelay = 0, CancellationToken cancellationToken = default)
        {
            using var workflowExecution = new WorkflowExecution(workflow.Id, applicationName);
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

                    var stepResult = await step.ExecuteAsync(context);

                    if (stepResult.InternalLogs != null && stepResult.InternalLogs.Any())
                    {
                        foreach (var log in stepResult.InternalLogs)
                        {
                            await AddLogWithNotification(workflowExecution, "Debug", log, step.Id, step.Name, cancellationToken: cancellationToken);
                        }
                    }

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

            // Populate OutputVariables from Variables before returning
            PopulateOutputVariables(workflow);

            // Copy OutputVariables to WorkflowExecution for return to caller
            workflowExecution.OutputVariables = workflow.OutputVariables;

            return workflowExecution;
        }

        /// <summary>
        /// Populate flow output variables with actual values from runtime variables.
        /// Supports both direct name matching and variable references (%variablename%).
        /// </summary>
        protected virtual void PopulateOutputVariables(Workflow workflow)
        {
            if (workflow.OutputVariables == null || !workflow.OutputVariables.Any())
            {
                logger.Debug("No output variables defined for workflow {WorkflowId}", workflow.Id);
                return;
            }

            var variableResolver = new StepVariableParser();

            foreach (var outputVar in workflow.OutputVariables.Values)
            {
                try
                {
                    // Check if Value contains %variablename% reference
                    var valueStr = outputVar.Value?.ToString();
                    if (!string.IsNullOrEmpty(valueStr) && valueStr.Contains("%"))
                    {
                        // Resolve variable reference (e.g., "%headlines%" ? actual value)
                        var resolvedValue = variableResolver.Parse(outputVar, workflow);
                        outputVar.Value = resolvedValue;

                        logger.Information(
                            "Resolved output variable '{Name}' from reference '{Original}' to value", 
                            outputVar.Name, 
                            valueStr);
                    }
                    else
                    {
                        // Try to find matching runtime variable by name
                        var runtimeVar = workflow.Variables.Values
                            .FirstOrDefault(v => v.Name.Equals(outputVar.Name, StringComparison.OrdinalIgnoreCase));

                        if (runtimeVar != null)
                        {
                            outputVar.Value = runtimeVar.Value;
                            outputVar.Type = runtimeVar.Type;

                            logger.Information(
                                "Populated output variable '{Name}' with value from runtime variable", 
                                outputVar.Name);
                        }
                        else
                        {
                            logger.Warning(
                                "Output variable '{Name}' defined but no matching runtime variable found", 
                                outputVar.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, 
                        "Failed to populate output variable '{Name}'", 
                        outputVar.Name);
                }
            }
        }

        /// <summary>
        /// Common workflow execution persistence logic - saves execution results
        /// </summary>
        protected virtual async Task SaveWorkflowExecution(WorkflowExecution workflowExecution)
        {
            await persistenceService.SaveWorkflowExecutionAsync(workflowExecution);
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
        /// (e.g., custom notifications, additional processing, etc.)
        /// Note: Execution persistence is now handled by the base class automatically
        /// </summary>
        protected virtual async Task OnExecutionCompleted(WorkflowExecution workflowExecution)
        {
            await Task.CompletedTask;
        }
    }
}