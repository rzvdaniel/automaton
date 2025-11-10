using Automaton.Studio.Domain;
using Automaton.Studio.Events;
using Automaton.Studio.Factories;
using Automaton.Studio.Pages.Flows.Designer.Components.StepExplorer;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.Flows.FlowDesigner;

public class DesignerViewModel
{
    private const int FlowDelay = 100;

    private readonly StepFactory stepFactory;
    private readonly FlowService flowService;
    private readonly StudioFlowConvertService flowConvertService;
    private readonly StudioFlowExecuteService workflowExecuteService;
    private readonly ConfigurationService configurationService;
    private CancellationTokenSource cancelFlowTokenSource;
    private bool flowRunning;
    public IEnumerable<Guid> SelectedRunners { get; set; } = new List<Guid>();
    public bool HasSelectedRunners => SelectedRunners.Any();
    public bool CanRunFlow => HasSelectedRunners;

    public StudioFlow Flow { get; set; }
    public StudioDefinition ActiveDefinition { get; set; }

    /// <summary>
    /// Gets the message to show when the run button is disabled
    /// </summary>
    public string GetRunButtonDisabledMessage()
    {        
        if (!HasSelectedRunners)
            return "Select one or more runners to execute this workflow";
        
        return string.Empty;
    }

    /// <summary>
    /// Determines what action to take when run button is clicked while disabled
    /// </summary>
    public string GetRunButtonAction()
    {        
        if (!HasSelectedRunners)
            return "select-runners";
        
        return "run";
    }

    public event EventHandler<StepEventArgs> StepCreated;

    public DesignerViewModel
    (
        ConfigurationService configurationService,
        StepFactory stepFactory,
        FlowService flowService,
        StudioFlowConvertService flowConvertService,
        StudioFlowExecuteService workflowExecuteService
    )
    {
        this.configurationService = configurationService;
        this.stepFactory = stepFactory;
        this.flowService = flowService;
        this.workflowExecuteService = workflowExecuteService;
        this.flowConvertService = flowConvertService;

        Flow = new StudioFlow();
    }

    public async Task LoadFlow(Guid flowId)
    {
        Flow = await flowService.Load(flowId);

        ActiveDefinition = Flow.GetStartupDefinition();  
    }

    public async Task SaveFlow()
    {
        await flowService.Update(Flow);
    }

    public void StopFlow()
    {
        cancelFlowTokenSource.Cancel();
    }

    public StudioDefinition CreateDefinition(string name)
    {
        var definition = Flow.CreateDefinition(name);

        return definition;
    }

    public IEnumerable<string> GetDefinitionNames()
    {
        return Flow.GetDefinitionNames();
    }

    public void SetActiveDefinition(StudioDefinition definition)
    {
        ActiveDefinition = definition;
    }

    public void SetActiveDefinition(string id)
    {
        ActiveDefinition = Flow.Definitions.SingleOrDefault(x => x.Id == id);
    }

    public StudioDefinition GetActiveDefinition()
    {
        return ActiveDefinition;
    }

    public string GetStartupDefinitionId()
    {
        return Flow?.StartupDefinitionId;
    }

    public IEnumerable<StudioDefinition> GetFlowDefinitions()
    {
        return Flow != null ? Flow.Definitions : Enumerable.Empty<StudioDefinition>();
    }

    public void CreateStep(StepExplorerModel customStepModel)
    {
        var step = customStepModel is CustomStepExplorerModel ?
            stepFactory.CreateCustomStep(customStepModel as CustomStepExplorerModel, ActiveDefinition) : 
            stepFactory.CreateStep(customStepModel.Name, ActiveDefinition);
        
        StepCreated?.Invoke(this, new StepEventArgs(step));
    }

    public void DeleteStep(StudioStep step)
    {
        ActiveDefinition.DeleteStep(step);
    }

    public void FinalizeStep(StudioStep step)
    {
        ActiveDefinition.CompleteStep(step);
    }

    public void UpdateStepConnections()
    {
        ActiveDefinition.UpdateStepConnections();
    }

    public void SetExecutingStep(string stepId)
    {
        Flow.SetExecutingStep(stepId);
    }

    public bool IsFlowNotRunning()
    {
        return !flowRunning;
    }
}
