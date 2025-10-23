using Automaton.Core.Attributes;
using Automaton.Core.Models;
using Automaton.Core.Scripting.Engines;

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
        try
        {
            this.scriptHost.ScriptTextWritten += OnScriptTextWritten;

            var resource = new ScriptResource()
            {
                ContentType = GetContentType(),
                Content = Code
            };

            var inputVariablesDictionary = CodeInputVariables.ToDictionary(x => x.Name, x => x.Value);

            var scriptVariables = scriptHost.Execute(resource, inputVariablesDictionary);

            foreach (var variable in Outputs)
            {
                if (scriptVariables.ContainsKey(variable.Value.Id))
                {
                    variable.Value.Value = scriptVariables[variable.Value.Id].ToString();
                    context.Workflow.SetVariable(variable.Value);
                }
            }

            return Task.FromResult(ExecutionResult.Next());
        }
        finally
        {
            scriptHost.ScriptTextWritten -= OnScriptTextWritten;
        }     
    }

    protected override void SetupProperties(StepExecutionContext context)
    {
        Code = GetInputValue<string>(nameof(Code));

        Language = GetInputValue<string>(nameof(Language));

        var codeInputVariables = GetInputValue<IList<StepVariable>>(nameof(CodeInputVariables));

        foreach (var variable in codeInputVariables)
        {
            var variableValue = StepVariableParser.Parse(variable, context.Workflow);
            variable.Value = variableValue;

            CodeInputVariables.Add(variable);
        }
    }

    private void OnScriptTextWritten(object sender, string text)
    {
        logger.Information("{Text}", text);
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
