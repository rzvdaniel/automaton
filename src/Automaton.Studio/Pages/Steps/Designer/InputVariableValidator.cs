using Automaton.Studio.Services;
using FluentValidation;

namespace Automaton.Studio.Pages.Steps.Designer;

public class InputVariableValidator : AbstractValidator<InputVariableModel>
{
    public InputVariableValidator(FlowsService flowService)
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50).WithMessage(Resources.Errors.NameRequired);
    }
}
