using Automaton.Node.Preparation;
using Microsoft.Extensions.Hosting;

namespace Automaton.Node;

public static class Program
{
    public const string HostStateDirectoryEnvironmentVariable = "AUTOMATON_NODE_HOST_STATE_DIRECTORY";

    public static async Task<int> Main()
    {
        var hostStateDirectory = Environment.GetEnvironmentVariable(HostStateDirectoryEnvironmentVariable);
        try
        {
            using var host = NodePreparationHost.CreateBuilder(hostStateDirectory).Build();
            await host.RunAsync();
            return 0;
        }
        catch (NodeInstallationLockException exception)
        {
            await Console.Error.WriteLineAsync($"Node startup blocked: {exception.Failure}.");
            return 2;
        }
    }
}
