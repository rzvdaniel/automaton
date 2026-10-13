using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Node.Preparation;

public static class NodePreparationHost
{
    public static HostApplicationBuilder CreateBuilder(string? hostStateDirectory = null) =>
        BuildHost(new NodeInstallationLock(hostStateDirectory));

    internal static HostApplicationBuilder CreateBuilderForTests(INodeInstallationLock installationLock) =>
        BuildHost(installationLock);

    private static HostApplicationBuilder BuildHost(INodeInstallationLock installationLock)
    {
        // No default environment, command-line, secrets or configuration providers.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.Sources.Clear();
        builder.Logging.AddSimpleConsole();
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None);
        builder.Services.AddSingleton<NodePreparationState>();
        builder.Services.AddSingleton(installationLock);
        builder.Services.AddHostedService<NodePreparationService>();
        return builder;
    }
}
