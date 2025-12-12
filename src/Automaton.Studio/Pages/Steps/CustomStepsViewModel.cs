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
    private StepFilterType currentFilter = StepFilterType.FlowSteps;
    private ICollection<CustomStepListItem>? cachedFilteredSteps = null;
    private int? cachedFlowStepsCount = null;
    private int? cachedAppStepsCount = null;
    private int? cachedAllStepsCount = null;

    public StepFilterType CurrentFilter 
    { 
        get => currentFilter;
        private set => currentFilter = value;
    }

    public ICollection<CustomStepListItem> CustomSteps 
    { 
        get
        {
            // Return cached result if filter hasn't changed
            if (cachedFilteredSteps != null)
                return cachedFilteredSteps;

            // Calculate and cache the filtered result
            cachedFilteredSteps = CurrentFilter switch
            {
                StepFilterType.All => allCustomSteps,
                StepFilterType.FlowSteps => allCustomSteps.Where(s => !s.IsAppOnlyStep).ToList(),
                StepFilterType.AppSteps => allCustomSteps.Where(s => s.IsAppOnlyStep).ToList(),
                _ => allCustomSteps
            };

            return cachedFilteredSteps;
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
        InvalidateCaches();
    }

    public void SetFilter(StepFilterType filter)
    {
        if (CurrentFilter != filter)
        {
            CurrentFilter = filter;
            cachedFilteredSteps = null; // Invalidate cache
        }
    }

    private void InvalidateCaches()
    {
        cachedFilteredSteps = null;
        cachedFlowStepsCount = null;
        cachedAppStepsCount = null;
        cachedAllStepsCount = null;
    }

    public int GetFlowStepsCount()
    {
        if (!cachedFlowStepsCount.HasValue)
        {
            cachedFlowStepsCount = allCustomSteps.Count(s => !s.IsAppOnlyStep);
        }
        return cachedFlowStepsCount.Value;
    }

    public int GetAppStepsCount()
    {
        if (!cachedAppStepsCount.HasValue)
        {
            cachedAppStepsCount = allCustomSteps.Count(s => s.IsAppOnlyStep);
        }
        return cachedAppStepsCount.Value;
    }

    public int GetAllStepsCount()
    {
        if (!cachedAllStepsCount.HasValue)
        {
            cachedAllStepsCount = allCustomSteps.Count;
        }
        return cachedAllStepsCount.Value;
    }

    public async Task CreateCustomStep(NewCustomStep customStep)
    {
        var step = await customStepsService.Create(customStep);
        var customStepModel = MapToCustomStepListItem(step);
        allCustomSteps.Add(customStepModel);
        stepFactory.AddCustomStep(step);
        InvalidateCaches();
    }

    public async Task DeleteCustomStep(Guid id)
    {
        await customStepsService.Delete(id);
        var customStep = allCustomSteps.SingleOrDefault(x => x.Id == id);
        if (customStep != null)
            allCustomSteps.Remove(customStep);
        stepFactory.RemoveStepFromExplorer(id);
        InvalidateCaches();
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
