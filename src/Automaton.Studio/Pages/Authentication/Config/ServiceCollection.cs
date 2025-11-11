using Automaton.Studio.Pages.Authentication.Login;
using Automaton.Studio.Pages.Authentication.Register;
using Automaton.Studio.Pages.Authentication.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Studio.Pages.Authentication.Config;

public static class ServiceCollection
{
    public static void AddAuthenticationApp(this IServiceCollection services, IConfiguration configuration)
    {
        // Models
        services.AddScoped<UserRegisterViewModel>();
        services.AddScoped<LoginViewModel>();

        // Services
        services.AddScoped(service => new ConfigurationService(configuration));
        services.AddScoped<AuthenticationService>();
        services.AddScoped<AccountService>();
    }
}
