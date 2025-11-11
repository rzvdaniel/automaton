
using Automaton.Studio.Pages.Account.Services;

namespace Automaton.Studio.Pages.Account;

public class AccountViewModel
{
    private readonly UserAccountService userAccountService;

    public string Name { get; set; }

    public string Email { get; set; }

    public string Password { get; set; }

    public string ConfirmPassword { get; set; }

    public AccountViewModel(UserAccountService userAccountService)
    {
        this.userAccountService = userAccountService;
    }

    public async Task UpdateUserProfile()
    {
        var userUpdate = new Models.UserProfile
        {
            Name = Name,
            Email = Email
        };

        await userAccountService.UpdateUserProfile(userUpdate);
    }
}
