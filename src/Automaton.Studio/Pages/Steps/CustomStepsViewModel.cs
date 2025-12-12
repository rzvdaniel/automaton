using Automaton.Studio.Factories;
using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.Steps;

public enum StepFilterType
{
    All,
    FlowSteps,
    AppSteps
}

public class CustomStepsViewModel
{
    private readonly CustomStepsService customStepsService;
    private readonly StepFactory stepFactory;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly ProjectContextService projectContextService;

    public CustomStepsViewModel(
        CustomStepsService customStepsService, 
        StepFactory stepFactory,
        ResourceAuthorizationService authorizationService,
        ProjectContextService projectContextService)
    {
        this.customStepsService = customStepsService;
        this.stepFactory = stepFactory;
        this.authorizationService = authorizationService;
        this.projectContextService = projectContextService;
    }

    private ICollection<CustomStepListItem> allCustomSteps = new List<CustomStepListItem>();
    public StepFilterType CurrentFilter { get; set; } = StepFilterType.FlowSteps;

    public ICollection<CustomStepListItem> CustomSteps 
    { 
        get
        {
            return CurrentFilter switch
            {
                StepFilterType.All => allCustomSteps,
                StepFilterType.FlowSteps => allCustomSteps.Where(s => !s.IsAppOnlyStep).ToList(),
                StepFilterType.AppSteps => allCustomSteps.Where(s => s.IsAppOnlyStep).ToList(),
                _ => allCustomSteps
            };
        }
    }

    /// <summary>
    /// Check if user can manage resources (update/delete) in the current context.
    /// </summary>
    public bool CanManageCustomSteps => authorizationService.CanManageResources();

    public async Task GetCustomSteps()
    {
        var projectId = projectContextService.GetCurrentProjectId();
        var flowsInfo = await customStepsService.List(projectId);
        allCustomSteps = flowsInfo.Select(MapToCustomStepListItem).ToList();
    }

    public void SetFilter(StepFilterType filter)
    {
        CurrentFilter = filter;
    }

    public int GetFlowStepsCount()
    {
        return allCustomSteps.Count(s => !s.IsAppOnlyStep);
    }

    public int GetAppStepsCount()
    {
        return allCustomSteps.Count(s => s.IsAppOnlyStep);
    }

    public int GetAllStepsCount()
    {
        return allCustomSteps.Count;
    }

    public async Task CreateCustomStep(NewCustomStep customStep)
    {
        var step = await customStepsService.Create(customStep);
        var customStepModel = MapToCustomStepListItem(step);
        allCustomSteps.Add(customStepModel);
        stepFactory.AddCustomStep(step);
    }

    public async Task DeleteCustomStep(Guid id)
    {
        await customStepsService.Delete(id);
        var customStep = allCustomSteps.SingleOrDefault(x => x.Id == id);
        if (customStep != null)
            allCustomSteps.Remove(customStep);
        stepFactory.RemoveStepFromExplorer(id);
    }

    private CustomStepListItem MapToCustomStepListItem(dynamic step)
    {
        // Accepts both CustomStep and CustomStep (from different layers)
        return new CustomStepListItem
        {
            Id = step.Id,
            Name = step.Name,
            Created = step.Created,
            Updated = step.Updated,
            LinkedAppCount = step.LinkedAppCount
        };
    }
}
