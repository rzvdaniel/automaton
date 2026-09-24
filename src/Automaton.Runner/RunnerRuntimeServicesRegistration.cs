using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console;

internal static class RunnerRuntimeServicesRegistration
{
    public static void Add(IServiceCollection services)
    {
        services.AddSingleton<RunnerRuntimeRegistry>();
        services.AddSingleton<IApplicationContainerRuntime>(provider =>
            RunnerApplicationContainerRuntimeSelection.UsesBroker(
                Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"),
                Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"),
                Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID"))
                ? new BrokeredApplicationContainerRuntime(
                    provider.GetRequiredService<HubService>(),
                    provider.GetRequiredService<FileStorageService>())
                : new DockerContainerRuntime());
        services.AddSingleton<IRunnerApplicationBuildExecutor, RunnerApplicationBuildExecutor>();
        services.AddSingleton<RunnerRuntimeContainerManager>();
        services.AddSingleton<IAppRuntimeProcessManager>(provider => provider.GetRequiredService<RunnerRuntimeContainerManager>());
        services.AddHostedService<RunnerRuntimeContainerHostedService>();
        services.AddSingleton<RunnerRuntimeProcessManager>();
        services.AddSingleton<HubService>();
        services.AddHostedService<StorageTelemetryBackgroundService>();
        services.AddHostedService(provider => provider.GetRequiredService<RunnerRuntimeGatewayService>());
    }
}
