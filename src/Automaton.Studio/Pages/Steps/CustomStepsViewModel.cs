using Automaton.Studio.Factories;
using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.Steps;

public class CustomStepsViewModel
{
    private readonly CustomStepsService customStepsService;
    private readonly StepFactory stepFactory;
    private readonly ResourceAuthorizationService authorizationService;

    public CustomStepsViewModel(
        CustomStepsService customStepsService, 
        StepFactory stepFactory,
        ResourceAuthorizationService authorizationService)
    {
        this.customStepsService = customStepsService;
        this.stepFactory = stepFactory;
        this.authorizationService = authorizationService;
    }

    public ICollection<CustomStepListItem> CustomSteps { get; set; } = new List<CustomStepListItem>();

    /// <summary>
    /// Check if user can manage resources (update/delete) in the current context.
    /// </summary>
    public bool CanManageCustomSteps => authorizationService.CanManageResources();

    public async Task GetCustomSteps()
    {
        var flowsInfo = await customStepsService.List();
        CustomSteps = flowsInfo.Select(MapToCustomStepListItem).ToList();
    }

    public async Task CreateCustomStep(NewCustomStep customStep)
    {
        var step = await customStepsService.Create(customStep);
        var customStepModel = MapToCustomStepListItem(step);
        CustomSteps.Add(customStepModel);
        stepFactory.AddCustomStep(step);
    }

    public async Task DeleteCustomStep(Guid id)
    {
        await customStepsService.Delete(id);
        var customStep = CustomSteps.SingleOrDefault(x => x.Id == id);
        if (customStep != null)
            CustomSteps.Remove(customStep);
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
            Updated = step.Updated
        };
    }
}
