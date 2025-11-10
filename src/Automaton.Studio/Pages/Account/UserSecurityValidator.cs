using FluentValidation;

namespace Automaton.Studio.Pages.Account;

public class UserSecurityValidator : AbstractValidator<UserSecurityViewModel>
{
    public UserSecurityValidator()
    {
        RuleFor(x => x.OldPassword).NotEmpty().MaximumLength(1024).WithMessage("Old password required");
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(1024).WithMessage("New password required");
    }
}
