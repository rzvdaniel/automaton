using Automaton.App.Authentication.Services;
using Automaton.Client.Auth.Models;

namespace Automaton.App.Authentication.Pages.Login;

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
