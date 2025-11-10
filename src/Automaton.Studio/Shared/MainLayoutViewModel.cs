using Automaton.Studio.Pages.Authentication.Services;

namespace Automaton.Studio.Shared
{
    public class MainLayoutViewModel
    {
        private readonly AuthenticationService authenticationService;

        public MainLayoutViewModel(AuthenticationService authenticationService)
        {
            this.authenticationService = authenticationService;
        }

        public async Task Logout()
        {
            await authenticationService.Logout();
        }
    }
}
