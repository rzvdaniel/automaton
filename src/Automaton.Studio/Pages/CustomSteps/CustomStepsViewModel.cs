using Automaton.Studio.Factories;
using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.CustomSteps;

public class CustomStepsViewModel
{
    private readonly CustomStepsService customStepsService;
    private readonly StepFactory stepFactory;

    public CustomStepsViewModel(CustomStepsService customStepsService, StepFactory stepFactory)
    {
        this.customStepsService = customStepsService;
        this.stepFactory = stepFactory;
    }

    public ICollection<CustomStepListItem> CustomSteps { get; set; } = new List<CustomStepListItem>();

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
