using Automaton.App.Account.Services;

namespace Automaton.App.Account.Account;

public class UserProfileViewModel
{
    private readonly UserAccountService userAccountService;

    public string Name { get; set; }

    public string Email { get; set; }

    public UserProfileViewModel(UserAccountService userAccountService)
    {
        this.userAccountService = userAccountService;
    }

    public async Task LoadUserProfile()
    {
        var userProfile = await userAccountService.GetUserProfile();

        Name = userProfile.Name; 
        Email = userProfile.Email;
    }

    public async Task UpdateUserProfile()
    {
        var userProfile = new Models.UserProfile
        {
            Name = Name,
            Email = Email
        };

        await userAccountService.UpdateUserProfile(userProfile);
    }
}
