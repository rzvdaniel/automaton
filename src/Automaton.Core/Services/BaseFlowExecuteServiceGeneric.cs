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
            IWorkflowExecutionPersistenceService? persistenceService = null)
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
        public virtual async Task<WorkflowExecution> Execute(TFlow flow, int executeDelay =0, CancellationToken cancellationToken = default)
        {
            var workflow = flowConvertService.ConvertFlow(flow);

            // Handle variable updates
            workflow.SetWorkflowVariable += async (sender, e) =>
            {
                await notificationService.SendVariableUpdate(e.Variable);
            };

            WorkflowExecution? workflowExecution = null;
            
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
                logger.Debug("Successfully saved workflow execution {ExecutionId}", workflowExecution.Id);
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
                logger.Debug("Successfully completed post-execution processing for execution {ExecutionId}", workflowExecution.Id);
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
        public virtual Task<WorkflowExecution> Execute(Workflow workflow, int executeDelay =0, CancellationToken cancellationToken = default)
        {
            return ExecuteWorkflow(workflow, executeDelay, cancellationToken);
        }

        /// <summary>
        /// IFlowExecuteService implementation - works with Flow (needs to be implemented by derived classes if supported)
        /// For backward compatibility - should be overridden to avoid ambiguous calls
        /// </summary>
        public virtual Task<WorkflowExecution> Execute(Flow flow, CancellationToken cancellationToken = default)
        {
            // Base implementation throws - derived classes can override if they support Flow conversion
            return Task.FromException<WorkflowExecution>(new NotSupportedException($"{GetType().Name} does not support direct Flow execution. Use the domain-specific Execute method instead."));
        }

        /// <summary>
        /// Core workflow execution logic
        /// </summary>
        protected virtual async Task<WorkflowExecution> ExecuteWorkflow(Workflow workflow, int executeDelay =0, CancellationToken cancellationToken = default)
        {
            using var workflowExecution = new WorkflowExecution(workflow.Id, applicationName);
            var definition = workflow.GetStartupDefinition()
                ?? throw new InvalidOperationException($"Workflow '{workflow.Id}' has no startup definition '{workflow.StartupDefinitionId}'.");
            var step = definition.GetFirstStep();

            // Set up logging context
            LogContext.PushProperty(LogContextProperties.WorkflowId, workflow.Id);
            LogContext.PushProperty(LogContextProperties.WorkflowExecutionId, workflowExecution.Id);
            LogContext.PushProperty(LogContextProperties.WorkflowName, workflow.Name);

            var startMessage = $"Start workflow: {workflow.Name}";
            await AddLogWithNotification(workflowExecution, "Debug", startMessage, cancellationToken: cancellationToken);

            while (step != null && !cancellationToken.IsCancellationRequested)
            {
                if (!await ExecuteStep(workflowExecution, workflow, definition, step, executeDelay, cancellationToken))
                    break;

                step = step.GetNextStep();
            }

            var endMessage = $"End workflow: {workflow.Name}";
            await AddLogWithNotification(workflowExecution, "Debug", endMessage, cancellationToken: cancellationToken);

            // Populate OutputVariables from Variables before returning
            PopulateOutputVariables(workflow);

            // Copy OutputVariables to WorkflowExecution for return to caller
            // KEY CHANGE: return outputs keyed by variable Name for downstream consumers (fallback to Id if name missing)
            var nameKeyedOutputs = new Dictionary<string, StepVariable>(StringComparer.OrdinalIgnoreCase);
            foreach (var sv in workflow.OutputVariables.Values)
            {
                var key = !string.IsNullOrWhiteSpace(sv.Name)
                    ? sv.Name
                    : sv.Id ?? throw new InvalidOperationException("An output variable must have a name or id.");
                // Last-in wins if duplicate names; ensures stable result
                nameKeyedOutputs[key] = sv;
            }
            workflowExecution.OutputVariables = nameKeyedOutputs;

            return workflowExecution;
        }

        private async Task<bool> ExecuteStep(
            WorkflowExecution workflowExecution,
            Workflow workflow,
            WorkflowDefinition definition,
            WorkflowStep step,
            int executeDelay,
            CancellationToken cancellationToken)
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
                    await Task.Delay(executeDelay, cancellationToken);

                await notificationService.SendStepExecution(step.Id);
                await LogStepStarted(workflowExecution, step, cancellationToken);
                var result = await step.ExecuteAsync(context);
                await LogInternalStepMessages(workflowExecution, step, result, cancellationToken);
                await AddLogWithNotification(
                    workflowExecution,
                    "Debug",
                    $"Completed step: {step.Name}",
                    step.Id,
                    step.Name,
                    cancellationToken: cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                await AddLogWithNotification(
                    workflowExecution,
                    "Warning",
                    "Workflow execution was canceled",
                    cancellationToken: cancellationToken);
                return false;
            }
            catch (Exception ex)
            {
                var errorMessage = $"Step {step.Name} with Id {step.Id} encountered an error. Message: {ex.Message}";
                await AddLogWithNotification(
                    workflowExecution,
                    "Error",
                    errorMessage,
                    step.Id,
                    step.Name,
                    ex,
                    cancellationToken);
                workflowExecution.HasErrors();
                return true;
            }
        }

        private Task LogStepStarted(
            WorkflowExecution workflowExecution,
            WorkflowStep step,
            CancellationToken cancellationToken)
        {
            var message = $"Executing step: {step.Name} (ID: {step.Id})";
            return AddLogWithNotification(
                workflowExecution,
                "Debug",
                message,
                step.Id,
                step.Name,
                cancellationToken: cancellationToken);
        }

        private async Task LogInternalStepMessages(
            WorkflowExecution workflowExecution,
            WorkflowStep step,
            ExecutionResult result,
            CancellationToken cancellationToken)
        {
            if (result.InternalLogs == null || !result.InternalLogs.Any())
                return;

            foreach (var message in result.InternalLogs)
            {
                await AddLogWithNotification(
                    workflowExecution,
                    "Debug",
                    message,
                    step.Id,
                    step.Name,
                    cancellationToken: cancellationToken);
            }
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

                        logger.Debug(
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

                            logger.Debug(
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
            string? stepId = null,
            string? stepName = null,
            Exception? exception = null,
            CancellationToken cancellationToken = default)
        {
            //1. Add to workflow execution for persistence
            workflowExecution.AddLog(level, message, stepId, stepName, exception);

            //2. Log to Serilog for server-side logging
            LogToSerilog(level, message, exception);

            //3. Send real-time notification
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

        protected virtual void LogToSerilog(string level, string message, Exception? exception = null)
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
