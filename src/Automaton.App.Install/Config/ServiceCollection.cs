using Automaton.App.Install.Pages.Register;
using Automaton.App.Install.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.App.Authentication.Config;

public static class ServiceCollection
{
    public static void AddInstallApp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<InstallViewModel>();
        services.AddScoped<InstallService>();
    }
}
