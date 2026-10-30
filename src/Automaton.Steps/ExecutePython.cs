using Automaton.Core.Attributes;
using Automaton.Core.Models;
using Automaton.Core.Scripting.Engines;

namespace Automaton.Steps;

public class ExecutePython : WorkflowStep
{
    private const string ContentType = @"text/x-python";

    private readonly ScriptEngineHost scriptHost;

    [IgnorePropertyParsing(true)]
    public string? Code { get; set; }

    [IgnorePropertyParsing(true)]
    public IList<StepVariable> CodeInputVariables { get; set; } = [];

    [IgnorePropertyParsing(true)]
    public IList<StepVariable> CodeOutputVariables { get; set; } = [];

    public ExecutePython(ScriptEngineHost scriptHost)
    {
        this.scriptHost = scriptHost;
    }

    protected override Task<ExecutionResult> RunAsync(StepExecutionContext context)
    {
        var resource = new ScriptResource()
        {
            ContentType = ContentType,
            Content = Code
        };

        // ✅ Convert input variables - avoid double-encoding strings
        var inputVariablesDictionary = CodeInputVariables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .ToDictionary(variable => variable.Name, variable => variable.Value ?? string.Empty);

        var execution = scriptHost.Execute(resource, inputVariablesDictionary);
        if (!execution.Success)
            throw new InvalidOperationException(execution.Error?.Message ?? "Python script execution failed.");

        SetCodeOutputVariables(context, execution.OutputVariables);

        return Task.FromResult(ExecutionResult.Next());
    }

    private void SetCodeOutputVariables(StepExecutionContext context, IDictionary<string, string> scriptVariables)
    {
        foreach (var variable in CodeOutputVariables)
        {
            if (scriptVariables.TryGetValue(variable.Name, out string? value))
            {
                variable.Value = value;
                context.Workflow.SetVariable(variable);
            }
        }
    }
}
