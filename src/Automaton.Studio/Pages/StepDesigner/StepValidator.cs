using Automaton.Studio.Domain;
using FluentValidation;

namespace Automaton.Studio.Pages.StepDesigner
{
    public class StepValidator : AbstractValidator<CustomStep>
    {
        public StepValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(256).WithMessage(Resources.Errors.NameRequired);
            RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(256).WithMessage(Resources.Errors.DisplayNameRequired);
        }
    }
}
