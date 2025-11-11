using Automaton.Studio.Models;
using Automaton.Studio.Services;
using FluentValidation;

namespace Automaton.Studio.Pages.Steps.Components.NewCustomStepDialog;

public class NewCustomStepValidator : AbstractValidator<NewCustomStep>
{
    private readonly CustomStepsService customStepsService;

    public NewCustomStepValidator(CustomStepsService customStepsService)
    {
        this.customStepsService = customStepsService;

        RuleFor(x => x.Name).NotEmpty().MaximumLength(50).WithMessage(Resources.Errors.NameRequired);
    }
}
