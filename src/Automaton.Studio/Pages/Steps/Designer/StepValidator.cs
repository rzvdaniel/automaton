using Automaton.Studio.Domain;
using FluentValidation;

namespace Automaton.Studio.Pages.Steps.Designer
{
    public class StepValidator : AbstractValidator<CustomStep>
    {
        public StepValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(256).WithMessage(Resources.Errors.NameRequired);
        }
    }
}
