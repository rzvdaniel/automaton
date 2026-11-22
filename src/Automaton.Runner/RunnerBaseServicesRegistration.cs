using Automaton.Runner.Console.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console;

internal static class RunnerBaseServicesRegistration
{
    public static void Add(IServiceCollection services)
    {
        services.AddSingleton<ConfigurationService>();
        services.AddSingleton<FileStorageService>();
        services.AddSingleton<ApiKeyAuthenticationService>();
        services.AddSingleton<RunnerWorkspaceService>();
        services.AddSingleton<AppActionDeploymentService>();
        services.AddSingleton<AppActionExecutionService>();
        services.AddSingleton<AppActionExecutionCoordinator>();
        services.AddHostedService<AppActionArtifactCleanupService>();
        services.AddSingleton<RunnerDeploymentService>();
        services.AddSingleton<RunnerHostService>();
    }
}
