using Automaton.Client.Auth.Models;
using Automaton.Studio.Pages.Authentication.Services;

namespace Automaton.Studio.Pages.Authentication.Login;

public class LoginViewModel
{
    private readonly AuthenticationService authenticationService;

    public LoginModel LoginModel { get; set;  } = new LoginModel();

    public LoginViewModel(AuthenticationService authenticationService)
    {
        this.authenticationService = authenticationService;
    }

    public async Task Login()
    {
        var loginDetails = new LoginDetails()
        {
            Email = LoginModel.Email,
            Password = LoginModel.Password,
            RememberMe = LoginModel.RememberMe
        };

        await authenticationService.Login(loginDetails);
    }
}
