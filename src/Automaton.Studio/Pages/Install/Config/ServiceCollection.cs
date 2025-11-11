using Automaton.Studio.Pages.Install.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Studio.Pages.Install.Config;

public static class ServiceCollection
{
    public static void AddInstallApp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<InstallViewModel>();
        services.AddScoped<InstallService>();
    }
}
