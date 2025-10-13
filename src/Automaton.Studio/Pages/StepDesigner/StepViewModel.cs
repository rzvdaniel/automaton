using Automaton.Core.Models;
using Automaton.Studio.Domain;
using Automaton.Studio.Factories;
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using System.Text;

namespace Automaton.Studio.Pages.StepDesigner;

public class StepViewModel : IDisposable
{
    private StepFactory stepFactory;
    private readonly CustomStepsService customStepsService;
    private readonly CustomStepExecuteService customStepExecuteService;
    private readonly ConfigurationService configurationService;
    private CustomStepDefinition CustomStepDefinition => CustomStep.Definition;

    public CustomStep CustomStep { get; set; } = new();
    public StringBuilder ScriptOutput { get; set; } = new StringBuilder();

    public event EventHandler ScriptTextWritten;

    /// <summary>
    /// Determines if running in debug mode (development)
    /// </summary>
    public bool IsDebugMode
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Determines if running on desktop platform
    /// </summary>
    public bool IsDesktopMode => configurationService.IsDesktop;

    /// <summary>
    /// Determines if we're in a development environment (regardless of platform)
    /// </summary>
    public bool IsDevelopmentEnvironment => IsDebugMode;

    /// <summary>
    /// Determines if local execution should be allowed
    /// Priority: Desktop always allowed, then development mode for testing
    /// </summary>
    public bool CanExecuteLocally => IsDesktopMode || IsDevelopmentEnvironment;

    /// <summary>
    /// Gets the message to show when the run button is disabled
    /// </summary>
    public string GetRunButtonDisabledMessage(bool hasSelectedRunners, bool hasAvailableRunners)
    {
        // Debug and desktop modes - should always be enabled
        if (CanExecuteLocally)
            return string.Empty;

        // Web release mode needs runners
        if (!hasAvailableRunners)
            return "No runners available - Install a runner to execute steps";
        
        if (!hasSelectedRunners)
            return "Select one or more runners to execute this step";
        
        return string.Empty;
    }

    /// <summary>
    /// Determines what action to take when run button is clicked while disabled
    /// </summary>
    public string GetRunButtonAction(bool hasSelectedRunners, bool hasAvailableRunners)
    {
        if (CanExecuteLocally)
            return "run";

        if (!hasAvailableRunners)
            return "download-runner";
        
        if (!hasSelectedRunners)
            return "select-runners";
        
        return "run";
    }

    /// <summary>
    /// Determines what execution modes are available
    /// </summary>
    public ExecutionCapabilities GetExecutionCapabilities(bool hasSelectedRunners)
    {
        return new ExecutionCapabilities
        {
            CanRunLocally = CanExecuteLocally,
            HasSelectedRunners = hasSelectedRunners
        };
    }

    public StepViewModel(CustomStepsService customStepsService, CustomStepExecuteService customStepExecuteService, StepFactory stepFactory, ConfigurationService configurationService)
    {
        this.stepFactory = stepFactory;
        this.customStepsService = customStepsService;
        this.customStepExecuteService = customStepExecuteService;
        this.configurationService = configurationService;
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

    public void Execute()
    {
        if (!CanExecuteLocally)
        {
            throw new Exception("Cannot execute step locally from designer");
        }

        if (ScriptOutput.Length > 0)
        {
            ScriptOutput.AppendLine();
        }
        ScriptOutput.AppendLine($"=== Execution started at {DateTime.Now:HH:mm:ss} ===");

        customStepExecuteService.Execute(CustomStep);
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
