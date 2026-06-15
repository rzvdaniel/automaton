using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Steps.Config;

namespace Automaton.Steps;

public class ExecuteFlow : WorkflowStep
{
    private readonly ConfigService configService;
    private readonly HttpClient httpClient;
    private readonly FlowExecuteService flowExecuteService;
    private readonly StepVariableParser variableParser;

    public Guid FlowId { get; set; }
    public IList<StepVariable> InputVariables { get; set; }
    public IList<StepVariable> OutputVariables { get; set; }

    public ExecuteFlow
    (
        ConfigService configService, 
        HttpClient httpClient,
        FlowExecuteService workflowExecuteService
    )
    {
        this.configService = configService;
        this.httpClient = httpClient;
        this.flowExecuteService = workflowExecuteService;
        this.variableParser = new StepVariableParser();
    }

    protected override async Task<ExecutionResult> RunAsync(StepExecutionContext context)
    {
        try
        {
            // Load the child flow
            var childFlow = await Load(FlowId);

            if (childFlow == null)
            {
                logger.Error("Failed to load child flow {FlowId}", FlowId);
                return ExecutionResult.Stop();
            }

            logger.Debug("Executing child flow '{FlowName}' (ID: {FlowId})", childFlow.Name, FlowId);

            // ✅ Map parent workflow variables → child flow InputVariables
            MapParentToChildInputs(context.Workflow, childFlow);

            // ✅ Execute child flow
            var childExecution = await flowExecuteService.Execute(childFlow, CancellationToken.None);

            logger.Debug(
                "Child flow '{FlowName}' completed with status: {Status}", 
                childFlow.Name, 
                childExecution.Status);

            // ✅ Map child flow OutputVariables → parent workflow variables
            MapChildToParentOutputs(childFlow, context.Workflow);

            return ExecutionResult.Next();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error executing child flow {FlowId}", FlowId);
            return ExecutionResult.Stop();
        }
    }

    private async Task<Flow> Load(Guid id)
    {
        var response = await httpClient.GetAsync($"{configService.ApiConfig.FlowsUrl}/{id}");
        var flow = await response.Content.ReadAsAsync<Flow>();

        return flow;
    }

    /// <summary>
    /// Map parent workflow variables to child flow InputVariables.
    /// Supports %variable% references for flexible mapping.
    /// </summary>
    private void MapParentToChildInputs(Workflow parentWorkflow, Flow childFlow)
    {
        if (InputVariables == null || !InputVariables.Any())
        {
            logger.Debug("No input variable mappings defined for ExecuteFlow step");
            return;
        }

        logger.Debug("Mapping {Count} input variables from parent to child flow", InputVariables.Count);

        foreach (var inputMapping in InputVariables)
        {
            try
            {
                // Resolve value from parent workflow (supports %variable% references)
                var resolvedValue = variableParser.Parse(inputMapping, parentWorkflow);

                // Find matching InputVariable in child flow by name
                var childInputVar = childFlow.InputVariables.Values
                    .FirstOrDefault(v => v.Name.Equals(inputMapping.Name, StringComparison.OrdinalIgnoreCase));

                if (childInputVar != null)
                {
                    childInputVar.Value = resolvedValue;

                    logger.Debug(
                        "Mapped input variable '{Name}': {ParentValue} → child flow",
                        inputMapping.Name,
                        inputMapping.Value);
                }
                else
                {
                    logger.Warning(
                        "Input variable '{Name}' not found in child flow InputVariables",
                        inputMapping.Name);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to map input variable '{Name}'", inputMapping.Name);
            }
        }
    }

    /// <summary>
    /// Map child flow OutputVariables back to parent workflow Variables.
    /// Makes child outputs available to subsequent steps in parent flow.
    /// </summary>
    private void MapChildToParentOutputs(Flow childFlow, Workflow parentWorkflow)
    {
        if (OutputVariables == null || !OutputVariables.Any())
        {
            logger.Debug("No output variable mappings defined for ExecuteFlow step");
            return;
        }

        logger.Debug("Mapping {Count} output variables from child to parent flow", OutputVariables.Count);

        foreach (var outputMapping in OutputVariables)
        {
            try
            {
                // Find matching OutputVariable in child flow by name
                var childOutputVar = childFlow.OutputVariables.Values
                    .FirstOrDefault(v => v.Name.Equals(outputMapping.Name, StringComparison.OrdinalIgnoreCase));

                if (childOutputVar != null)
                {
                    // Set value in parent workflow Variables (makes it available to next steps)
                    outputMapping.Value = childOutputVar.Value;
     
                    parentWorkflow.SetVariable(outputMapping);

                    logger.Debug(
                        "Mapped output variable '{Name}': child flow → parent Variables",
                        outputMapping.Name);
                }
                else
                {
                    logger.Warning(
                        "Output variable '{Name}' not found in child flow OutputVariables",
                        outputMapping.Name);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to map output variable '{Name}'", outputMapping.Name);
            }
        }
    }
}
