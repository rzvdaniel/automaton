using Automaton.Studio.Factories;
using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.Steps;

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

    /// <summary>
    /// Flow steps - steps NOT linked to any App (available in Flow designer)
    /// </summary>
    public ICollection<CustomStepListItem> FlowSteps { get; set; } = new List<CustomStepListItem>();

    /// <summary>
    /// Check if user can manage resources (update/delete) in the current context.
    /// </summary>
    public bool CanManageCustomSteps => authorizationService.CanManageResources();

    public async Task GetCustomSteps()
    {
        var projectId = projectContextService.GetCurrentProjectId();
        
        // Load Flow steps only (NOT linked to any App)
        var flowSteps = await customStepsService.List(projectId, excludeAppLinked: true);
        FlowSteps = flowSteps.Select(MapToCustomStepListItem).ToList();
    }

    public async Task CreateCustomStep(NewCustomStep customStep)
    {
        var step = await customStepsService.Create(customStep);
        var customStepModel = MapToCustomStepListItem(step);
        
        FlowSteps.Add(customStepModel);
        stepFactory.AddCustomStep(step);
    }

    public async Task DeleteCustomStep(Guid id)
    {
        await customStepsService.Delete(id);
        
        var flowStep = FlowSteps.SingleOrDefault(x => x.Id == id);
        if (flowStep != null)
        {
            FlowSteps.Remove(flowStep);
            stepFactory.RemoveStepFromExplorer(id);
        }
    }

    private CustomStepListItem MapToCustomStepListItem(dynamic step)
    {
        return new CustomStepListItem
        {
            Id = step.Id,
            Name = step.Name,
            Created = step.Created,
            Updated = step.Updated,
            WebAppId = step.WebAppId,
            WebAppName = step.WebAppName
        };
    }
}
