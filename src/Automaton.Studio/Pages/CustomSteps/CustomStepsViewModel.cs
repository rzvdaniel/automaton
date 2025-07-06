using AutoMapper;
using Automaton.Studio.Factories;
using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.CustomSteps;

public class CustomStepsViewModel(CustomStepsService customStepsService, StepFactory stepFactory, IMapper mapper)
{
    public ICollection<CustomStepListItem> CustomSteps { get; set;  } = new List<CustomStepListItem>();

    public async Task GetCustomSteps()
    {
        var flowsInfo = await customStepsService.List();
        CustomSteps = mapper.Map<ICollection<CustomStepListItem>>(flowsInfo);
    }

    public async Task CreateCustomStep(NewCustomStep customStep)
    {
        var step = await customStepsService.Create(customStep);
        
        var customStepModel = mapper.Map<CustomStepListItem>(step);
        CustomSteps.Add(customStepModel);

        stepFactory.AddCustomStep(step);
    }

    public async Task DeleteCustomStep(Guid id)
    {
        await customStepsService.Delete(id);

        var customStep = CustomSteps.SingleOrDefault(x => x.Id == id);

        CustomSteps.Remove(customStep);

        stepFactory.RemoveStepFromExplorer(id);
    }
}
