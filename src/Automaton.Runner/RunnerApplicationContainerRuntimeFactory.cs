using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console;

internal static class RunnerApplicationContainerRuntimeFactory
{
    public static IApplicationContainerRuntime Create(IServiceProvider provider)
    {
        if (!RunnerApplicationContainerRuntimeSelection.UsesBroker(
                Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"),
                Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"),
                Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID")))
            return new DockerContainerRuntime();

        return new BrokeredApplicationContainerRuntime(
            provider.GetRequiredService<HostContainerExecutionApiClient>().ExecuteAsync,
            () => provider.GetRequiredService<FileStorageService>().LoadInstallationProfileAsync());
    }
}
