using Automaton.Studio.Models;
using Automaton.Studio.Resources;
using FluentValidation;

namespace Automaton.Studio.Pages.Flows.Designer;

public class FlowScheduleModelValidator : AbstractValidator<FlowScheduleModel>
{
    public FlowScheduleModelValidator()
    {
        RuleFor(x => x.RunnerIds).NotEmpty().WithMessage(Errors.RunnerRequiredToScheduleFlow);
    }
}
