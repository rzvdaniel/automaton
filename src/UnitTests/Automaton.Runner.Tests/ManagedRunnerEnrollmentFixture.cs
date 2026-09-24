using System.Net.Http;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Automaton.Runner.Console.Tests;

internal static class ManagedRunnerEnrollmentFixture
{
    public static RunnerHostService CreateRunner(IHost host, HttpClient enrollmentClient)
        => new(
            host.Services.GetRequiredService<ConfigurationService>(),
            host.Services.GetRequiredService<FileStorageService>(),
            host.Services.GetRequiredService<ApiKeyAuthenticationService>(),
            host.Services.GetRequiredService<HubService>(),
            enrollmentClient,
            host.Services.GetRequiredService<ScriptEngineSettingsReporter>(),
            host.Services.GetRequiredService<RunnerWorkspaceService>());
}
