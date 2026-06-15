using Automaton.Core.Attributes;
using Automaton.Core.Extensions;
using Serilog;
using System.Reflection;

namespace Automaton.Core.Models;

public abstract class WorkflowStep
{
    protected readonly ILogger logger;

    public virtual string Id { get; set; }

    public virtual string Name { get; set; }

    public string DisplayName { get; set; }

    public string Description { get; set; }

    public string? ParentId { get; set; }

    public string Type { get; set; }

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
        var stepClone = step.CloneJson();

        Id = stepClone.Id;
        Name = stepClone.Name;
        DisplayName = stepClone.DisplayName;
        Description = stepClone.Description;
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
        return NextStepId != null ? WorkflowDefinition.Steps[NextStepId] : null;
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
        foreach (var input in Inputs)
        {
            var stepType = GetType();
            var stepProperty = stepType.GetProperty(input.Value.Name);
            var variable = input.Value;
            var value = variable.Value;

            // Auto-populate step inputs from Workflow.Variables
            if (string.IsNullOrEmpty(value))
            {
                var runtimeVar = context.Workflow.Variables.Values
                    .FirstOrDefault(v => v.Name.Equals(variable.Name, StringComparison.OrdinalIgnoreCase));

                if (runtimeVar != null)
                {
                    value = runtimeVar.Value;

                    logger.Debug(
                        "Auto-populated step input '{Name}' from workflow Variables",
                        variable.Name);
                }
            }

            // Do string replacement parsing if not ignored
            if (ShouldParseProperty(stepProperty))
            {
                variable.Value = value = StepVariableParser.Parse(variable, context.Workflow);
            }

            if (value == null)
            {
                continue;
            }

            // Only strings are supported on public step properties
            if (stepProperty.PropertyType == typeof(string))
            {
                stepProperty.SetValue(this, value);
            }
            else
            {
                // If step exposes non-string property, attempt simple Guid coercion
                if (stepProperty.PropertyType == typeof(Guid))
                {
                    if (Guid.TryParse(value, out var guidValue))
                    {
                        stepProperty.SetValue(this, guidValue);
                        continue;
                    }
                }

                logger.Error("Type mismatch: Cannot assign string to property of type {Type}", stepProperty.PropertyType);
                throw new InvalidCastException($"Cannot assign string to property of type {stepProperty.PropertyType}");
            }
        }
    }

    private static bool ShouldParseProperty(PropertyInfo stepProperty)
    {
        var propertyParsing = stepProperty.GetCustomAttributes<IgnorePropertyParsing>().SingleOrDefault();

        return propertyParsing == null || !propertyParsing.Ignore;
    }
}
