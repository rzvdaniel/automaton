using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using FluentValidation;

namespace Automaton.Studio.Server.Validators
{
    public class RunnerDetailsValidator : AbstractValidator<RunnerDetails>
    {
        private readonly RunnerService runnerService;

        public RunnerDetailsValidator(RunnerService runnerService)
        {
            this.runnerService = runnerService;

            RuleFor(x => x.Name).NotEmpty().MaximumLength(50).WithMessage(Resources.Errors.NameRequired);

            When(x => !string.IsNullOrEmpty(x.Name), () => {
                RuleFor(x => x).Must(NameIsUniqueOrSameRunner).WithMessage(Resources.Errors.RunnerExists);
            });     
        }

        private bool NameIsUniqueOrSameRunner(RunnerDetails runner)
        {
            // Allow update if this is an existing runner with the same ID
            if (runner.Id != Guid.Empty && runnerService.ExistsById(runner.Id))
            {
                // This is an update - check if the name conflicts with another runner
                var existingRunner = runnerService.Get(runner.Id);
                if (existingRunner != null && existingRunner.Name.Equals(runner.Name, StringComparison.OrdinalIgnoreCase))
                {
                    // Same runner, same name - allow
                    return true;
                }
                
                // Check if the new name conflicts with another runner
                return !runnerService.Exists(runner.Name);
            }

            // New runner - check name uniqueness
            return !runnerService.Exists(runner.Name);
        }
    }
}
