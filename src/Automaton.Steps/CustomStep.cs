using Automaton.Core.Attributes;
using Automaton.Core.Models;
using Automaton.Core.Scripting.Engines;
using Automaton.Core.Utilities;
using Automaton.Steps.Enums;
using Newtonsoft.Json;

namespace Automaton.Steps;

public class CustomStep : WorkflowStep
{
    private readonly ScriptEngineHost scriptHost;

    [IgnorePropertyParsing(true)]
    public string? Code { get; set; }

    public string Language { get; set; }

    private StepVariableParser StepVariableParser { get; } = new StepVariableParser();

    public IList<StepVariable> CodeInputVariables { get; set; }

    public CustomStep(ScriptEngineHost scriptHost)
    {
        this.scriptHost = scriptHost;
        CodeInputVariables = [];
    }

    protected override Task<ExecutionResult> RunAsync(StepExecutionContext context)
    {
        var result = ExecutionResult.Next();
        var logs = new List<string>();

        void OnScriptTextWrittenLocal(object sender, string text)
        {
            logger.Information("{Text}", text);
            logs.Add(text);
        }

        try
        {
            scriptHost.ScriptTextWritten += OnScriptTextWrittenLocal;

            var resource = new ScriptResource()
            {
                ContentType = GetContentType(),
                Content = Code
            };

            // ✅ Convert input variables to JSON strings
            // ⚠️ IMPORTANT: Only serialize non-string values
            // If value is already a string, pass it as-is to avoid double-encoding
            var inputVariablesDictionary = CodeInputVariables.ToDictionary(
                x => x.Name,
                x => InputVariableConverter.ConvertToString(x.Value));

            var scriptVariables = scriptHost.Execute(resource, inputVariablesDictionary);

            SetOutputVariables(context, scriptVariables);

            result.InternalLogs = logs;
            return Task.FromResult(result);
        }
        finally
        {
            scriptHost.ScriptTextWritten -= OnScriptTextWrittenLocal;
        }
    }

    private void SetOutputVariables(StepExecutionContext context, IDictionary<string, string> scriptVariables)
    {
        foreach (var variable in Outputs)
        {
            if (scriptVariables.ContainsKey(variable.Value.Name))
            {
                variable.Value.Value = scriptVariables[variable.Value.Name];

                context.Workflow.SetVariable(variable.Value);
            }
        }
    }

    protected override void SetupProperties(StepExecutionContext context)
    {
        Code = GetInputString(nameof(Code));
        Language = GetInputString(nameof(Language));

        var inputsVariable = Inputs.Values.FirstOrDefault(v => v.Name == nameof(CodeInputVariables));
        
        if (inputsVariable != null && inputsVariable.Value != null)
        {
            if (inputsVariable.Value is string s && s.TrimStart().StartsWith("{"))
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, StepVariable>>(s);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        var variable = kvp.Value;
                        var variableValue = StepVariableParser.Parse(variable, context.Workflow);
                        variable.Value = variableValue;
                        CodeInputVariables.Add(variable);
                    }
                }
            }
            else if (inputsVariable.Value is string json && json.TrimStart().StartsWith("["))
            {
                var list = JsonConvert.DeserializeObject<IList<StepVariable>>(json) ?? new List<StepVariable>();
                foreach (var variable in list)
                {
                    var variableValue = StepVariableParser.Parse(variable, context.Workflow);
                    variable.Value = variableValue;
                    CodeInputVariables.Add(variable);
                }
            }
        }
    }

    private string GetContentType()
    {
        var contentType = Language switch
        {
            CustomStepLanguage.Python => "text/x-python",
            CustomStepLanguage.Batch => "text/x-batch",
            _ => "text/x-python"
        };

        return contentType;
    }
}
