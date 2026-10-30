using Automaton.Core.Attributes;
using Automaton.Core.Extensions;
using Serilog;
using System.Reflection;

namespace Automaton.Core.Models;

public abstract class WorkflowStep
{
    protected readonly ILogger logger;

    public virtual string Id { get; set; } = string.Empty;

    public virtual string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? ParentId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? NextStepId { get; set; }

    public WorkflowDefinition? WorkflowDefinition { get; set; }

    public IDictionary<string, StepVariable> Inputs { get; set; } = new Dictionary<string, StepVariable>();

    public IDictionary<string, StepVariable> Outputs { get; set; } = new Dictionary<string, StepVariable>();

    protected abstract Task<ExecutionResult> RunAsync(StepExecutionContext context);

    private StepVariableParser StepVariableParser { get; } = new StepVariableParser();

    public WorkflowStep()
    {
        logger = Log.ForContext<WorkflowStep>();
    }

    public void Setup(Step step, WorkflowDefinition workflowDefinition)
    {
        // Need to deep clone the incoming step to avoid Inputs and Outputs
        // being updated during execution of the flow and then passed back to designer
        var stepClone = step.CloneJson() ?? throw new InvalidOperationException("The workflow step could not be cloned.");

        Id = stepClone.Id;
        Name = stepClone.Name;
        DisplayName = stepClone.DisplayName;
        Description = stepClone.Description ?? string.Empty;
        Type = stepClone.Type;
        NextStepId = stepClone.NextStepId;
        ParentId = stepClone.ParentId;
        Inputs = stepClone.Inputs;
        Outputs = stepClone.Outputs;
        WorkflowDefinition = workflowDefinition;
    }

    public virtual async Task<ExecutionResult> ExecuteAsync(StepExecutionContext context)
    {
        logger.Debug("Execute step: {0}", Name);

        SetupProperties(context);

        return await RunAsync(context);
    }

    public virtual WorkflowStep? GetNextStep()
    {
        return NextStepId != null && WorkflowDefinition != null
            && WorkflowDefinition.Steps.TryGetValue(NextStepId, out var nextStep)
                ? nextStep
                : null;
    }

    protected StepVariable? GetInputVariable(string name)
    {
        var input = Inputs.Values.FirstOrDefault(v => v.Name == name);

        return input;
    }

    protected string? GetInputString(string name)
    {
        return GetInputVariable(name)?.Value;
    }

    protected virtual void SetupProperties(StepExecutionContext context)
    {
        var stepType = GetType();
        foreach (var input in Inputs)
        {
            if (string.IsNullOrEmpty(input.Value.Name))
            {
                logger.Warning("Ignoring workflow input with no name for {StepType}", stepType.Name);
                continue;
            }

            var stepProperty = stepType.GetProperty(input.Value.Name);
            if (stepProperty == null)
            {
                logger.Warning("Ignoring workflow input {InputName} because {StepType} has no matching property", input.Value.Name, stepType.Name);
                continue;
            }
            var variable = input.Value;
            var value = ResolveInputValue(variable, stepProperty, context);
            AssignInputValue(stepProperty, value);
        }
    }

    private string? ResolveInputValue(StepVariable variable, PropertyInfo stepProperty, StepExecutionContext context)
    {
        var value = variable.Value;
        if (string.IsNullOrEmpty(value))
        {
            var runtimeVar = context.Workflow.Variables.Values
                .FirstOrDefault(item => string.Equals(item.Name, variable.Name, StringComparison.OrdinalIgnoreCase));
            if (runtimeVar != null)
            {
                value = runtimeVar.Value;
                logger.Debug(
                    "Auto-populated step input '{Name}' from workflow Variables",
                    variable.Name);
            }
        }

        if (ShouldParseProperty(stepProperty))
            variable.Value = value = StepVariableParser.Parse(variable, context.Workflow);

        return value;
    }

    private void AssignInputValue(PropertyInfo stepProperty, string? value)
    {
        if (value == null)
            return;

        if (stepProperty.PropertyType == typeof(string))
        {
            stepProperty.SetValue(this, value);
            return;
        }

        if (stepProperty.PropertyType == typeof(Guid) && Guid.TryParse(value, out var guidValue))
        {
            stepProperty.SetValue(this, guidValue);
            return;
        }

        logger.Error("Type mismatch: Cannot assign string to property of type {Type}", stepProperty.PropertyType);
        throw new InvalidCastException($"Cannot assign string to property of type {stepProperty.PropertyType}");
    }

    private static bool ShouldParseProperty(PropertyInfo stepProperty)
    {
        var propertyParsing = stepProperty.GetCustomAttributes<IgnorePropertyParsing>().SingleOrDefault();

        return propertyParsing == null || !propertyParsing.Ignore;
    }
}
