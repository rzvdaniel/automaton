using AutoMapper;
using Automaton.Core.Models;
using Automaton.Studio.Domain;
using Automaton.Studio.Events;
using Automaton.Studio.Factories;
using Automaton.Studio.Models;
using Automaton.Studio.Pages.FlowDesigner.Components.StepExplorer;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.FlowDesigner;

public class DesignerViewModel
{
    private const int FlowDelay = 100;

    private readonly IMapper mapper;
    private readonly StepFactory stepFactory;
    private readonly FlowService flowService;
    private readonly StudioFlowConvertService flowConvertService;
    private readonly StudioFlowExecuteService workflowExecuteService;
    private readonly ConfigurationService configurationService;
    private CancellationTokenSource cancelFlowTokenSource;
    private bool flowRunning;

    public StudioFlow Flow { get; set; }
    public StudioDefinition ActiveDefinition { get; set; }

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
    /// Determines if the run button should be visible at all
    /// - Always show the button to maintain consistent UI
    /// - Better UX than hiding it completely
    /// </summary>
    public bool ShouldShowRunButton(bool hasSelectedRunners)
    {
        // Always show the button for consistent UX
        // Instead of hiding it, we'll disable it with helpful messages
        return true;
    }

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
            return "No runners available - Install a runner to execute workflows";
        
        if (!hasSelectedRunners)
            return "Select one or more runners to execute this workflow";
        
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
    /// Determines if the run button should be enabled
    /// </summary>
    public bool CanRunFlow(bool hasSelectedRunners)
    {
        // Can run locally (desktop/debug) regardless of runner selection
        if (CanExecuteLocally)
            return true;

        // Web release mode: can only run if runners are selected
        return hasSelectedRunners;
    }

    /// <summary>
    /// Determines what execution modes are available
    /// </summary>
    public ExecutionCapabilities GetExecutionCapabilities(bool hasSelectedRunners)
    {
        return new ExecutionCapabilities
        {
            CanRunLocally = CanExecuteLocally,
            CanRunOnRunners = hasSelectedRunners,
            ShouldRunBoth = CanExecuteLocally && hasSelectedRunners
        };
    }

    public event EventHandler<StepEventArgs> StepCreated;

    public DesignerViewModel
    (
        IMapper mapper,
        ConfigurationService configurationService,
        StepFactory stepFactory,
        FlowService flowService,
        StudioFlowConvertService flowConvertService,
        StudioFlowExecuteService workflowExecuteService
    )
    {
        this.mapper = mapper;
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

    public async Task<WorkflowExecution> RunFlow()
    {
        if (!CanExecuteLocally)
        {
            throw new Exception("Can not execute flow locally from designer");
        }

        cancelFlowTokenSource = new CancellationTokenSource();

        flowRunning = true;

        try
        {
            var workflowExecution = await workflowExecuteService.Execute(Flow, FlowDelay, cancelFlowTokenSource.Token);
            return workflowExecution;
        }
        finally
        {
            flowRunning = false;
        }
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
