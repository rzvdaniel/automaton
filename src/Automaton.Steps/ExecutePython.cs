using Automaton.Core.Attributes;
using Automaton.Core.Models;
using Automaton.Core.Scripting.Engines;
using Automaton.Core.Utilities;
using Newtonsoft.Json;

namespace Automaton.Steps;

public class ExecutePython : WorkflowStep
{
    private const string ContentType = @"text/x-python";

    private readonly ScriptEngineHost scriptHost;

    [IgnorePropertyParsing(true)]
    public string? Code { get; set; }

    [IgnorePropertyParsing(true)]
    public IList<StepVariable> CodeInputVariables { get; set; }

    [IgnorePropertyParsing(true)]
    public IList<StepVariable> CodeOutputVariables { get; set; }

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
        // Only serialize non-string values; strings are passed as-is
        var inputVariablesDictionary = CodeInputVariables.ToDictionary(
            x => x.Name,
            x => InputVariableConverter.ConvertToString(x.Value));

        var scriptVariables = scriptHost.Execute(resource, inputVariablesDictionary);

        foreach (var variable in CodeOutputVariables)
        {
            if (scriptVariables.ContainsKey(variable.Name))
            {
                // ✅ scriptVariables values are already JSON strings
                variable.Value = scriptVariables[variable.Name];
                context.Workflow.SetVariable(variable);
            }
        }

        return Task.FromResult(ExecutionResult.Next());
    }
}
