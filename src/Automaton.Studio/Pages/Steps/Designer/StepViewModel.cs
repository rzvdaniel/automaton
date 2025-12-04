using Automaton.Core.Models;
using Automaton.Studio.Domain;
using Automaton.Studio.Factories;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using System.Text;

namespace Automaton.Studio.Pages.Steps.Designer;

public class StepViewModel : IDisposable
{
    private StepFactory stepFactory;
    private readonly CustomStepsService customStepsService;
    private readonly CustomStepExecuteService customStepExecuteService;
    private readonly ConfigurationService configurationService;
    private readonly RunnerService runnerService;
    private readonly ProjectContextService projectContextService;

    public Guid? SelectedRunner { get; set; }
    public bool HasSelectedRunner => SelectedRunner.HasValue && SelectedRunner.Value != Guid.Empty;
    public bool CanRunFlow => HasSelectedRunner;

    private CustomStepDefinition CustomStepDefinition => CustomStep.Definition;

    public CustomStep CustomStep { get; set; } = new();
    public StringBuilder ScriptOutput { get; set; } = new StringBuilder();

    public event EventHandler ScriptTextWritten;

    /// <summary>
    /// Gets the message to show when the run button is disabled
    /// </summary>
    public string GetRunButtonDisabledMessage()
    {
        if (!HasSelectedRunner)
            return "Select a runner to execute this step";
        
        return string.Empty;
    }

    /// <summary>
    /// Determines what action to take when run button is clicked while disabled
    /// </summary>
    public string GetRunButtonAction()
    {  
        if (!HasSelectedRunner)
            return "select-runners";
        
        return "run";
    }

    public StepViewModel(CustomStepsService customStepsService, CustomStepExecuteService customStepExecuteService, StepFactory stepFactory, ConfigurationService configurationService, RunnerService runnerService, ProjectContextService projectContextService)
    {
        this.stepFactory = stepFactory;
        this.customStepsService = customStepsService;
        this.customStepExecuteService = customStepExecuteService;
        this.configurationService = configurationService;
        this.runnerService = runnerService;
        this.projectContextService = projectContextService;
        customStepExecuteService.ScriptTextWritten += OnNewScriptText;
    }

    public async Task Load(Guid id)
    {
        CustomStep = await customStepsService.Load(id);
    }

    public async Task Save()
    {
        await customStepsService.Update(CustomStep);

        stepFactory.UpdateCustomStep(CustomStep);
    }

    /// <summary>
    /// Execute custom step on the selected runner
    /// </summary>
    public async Task<RunnerCustomStepResult> ExecuteOnRunner()
    {
        if (!SelectedRunner.HasValue || SelectedRunner.Value == Guid.Empty)
        {
            throw new ArgumentException("A runner must be selected", nameof(SelectedRunner));
        }

        // Add execution header to logs
        if (ScriptOutput.Length > 0)
        {
            ScriptOutput.AppendLine();
        }
        ScriptOutput.AppendLine($"=== Execution started at {DateTime.Now:HH:mm:ss} ===");
        ScriptOutput.AppendLine();
        ScriptTextWritten?.Invoke(this, EventArgs.Empty);

        // Execute on single runner using the multi-runner method with array of one
        var result = await customStepExecuteService.ExecuteOnRunner(CustomStep.Id, SelectedRunner.Value);

        if (result == null)
        {
            throw new InvalidOperationException("No result returned from runner execution");
        }

        // Get runner name for logs
        var projectId = projectContextService.GetCurrentProjectId();
        var runners = await runnerService.List(projectId);
        var runner = runners.FirstOrDefault(r => r.Id == result.RunnerId);
        var runnerName = runner?.Name ?? result.RunnerId.ToString();

        ScriptOutput.AppendLine($"--- Runner: {runnerName} ---");
        
        if (result.Success)
        {
            ScriptOutput.AppendLine($"✅ Status: SUCCESS");
            
            var executionTime = result.Finished - result.Started;
            ScriptOutput.AppendLine($"⏱️  Execution Time: {executionTime.TotalMilliseconds:F2}ms");
            ScriptOutput.AppendLine();
            
            // Show console output (stdout) - this contains print() statements
            if (!string.IsNullOrEmpty(result.Output))
            {
                ScriptOutput.AppendLine($"📋 Console Output:");
                ScriptOutput.AppendLine(result.Output);
            }

            // Show output variables and populate them in the CustomStep
            if (result.OutputVariables != null && result.OutputVariables.Any())
            {
                ScriptOutput.AppendLine($"📊 Output Variables:");
                foreach (var variable in result.OutputVariables)
                {
                    ScriptOutput.AppendLine($"  • {variable.Key}: {variable.Value}");
                    
                    // Populate the output variable in the CustomStep definition
                    var outputVar = CustomStep.Definition.CodeOutputVariables?.FirstOrDefault(v => v.Name == variable.Key);
                    if (outputVar != null)
                    {
                        outputVar.Value = variable.Value?.ToString();
                    }
                }
                ScriptOutput.AppendLine();
            }
        }
        else
        {
            ScriptOutput.AppendLine($"❌ Status: FAILED");
            ScriptOutput.AppendLine();
            
            if (!string.IsNullOrEmpty(result.Error))
            {
                ScriptOutput.AppendLine($"❗ Error:");
                ScriptOutput.AppendLine(result.Error);
                ScriptOutput.AppendLine();
            }
            
            // Show console output even on failure (may contain error messages)
            if (!string.IsNullOrEmpty(result.Output))
            {
                ScriptOutput.AppendLine($"📋 Console Output:");
                ScriptOutput.AppendLine(result.Output);
                ScriptOutput.AppendLine();
            }
        }

        // Add completion footer
        ScriptOutput.AppendLine($"=== Execution completed at {DateTime.Now:HH:mm:ss} ===");
        ScriptTextWritten?.Invoke(this, EventArgs.Empty);

        return result;
    }

    public void AddInputVariable()
    {
        var variableName = $"Variable{CustomStepDefinition.CodeInputVariables?.Count}";

        CustomStepDefinition.CodeInputVariables.Add(new StepVariable
        {
            Id = Guid.NewGuid().ToString(),
            Name = variableName
        });
    }

    public void DeleteInputVariable(string name)
    {
        var variable = CustomStepDefinition.CodeInputVariables.SingleOrDefault(x => x.Name == name);
        CustomStepDefinition.CodeInputVariables.Remove(variable);
    }

    public void AddOutputVariable()
    {
        var variableName = $"Variable{CustomStepDefinition.CodeOutputVariables?.Count}";

        CustomStepDefinition.CodeOutputVariables.Add(new StepVariable
        {
            Id = Guid.NewGuid().ToString(),
            Name = variableName 
        });
    }

    public void DeleteOutputVariable(string name)
    {
        var variable = CustomStepDefinition.CodeOutputVariables.SingleOrDefault(x => x.Name == name);
        CustomStepDefinition.CodeOutputVariables.Remove(variable);
    }

    public void ClearScriptOutputText()
    {
        ScriptOutput.Clear();
    }

    private void OnNewScriptText(object sender, string e)
    {
        ScriptOutput.Append(e);

        ScriptTextWritten?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        customStepExecuteService.ScriptTextWritten -= OnNewScriptText;
    }
}
