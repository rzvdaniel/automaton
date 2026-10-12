using Automaton.Node.Preparation;
using Microsoft.Extensions.Hosting;

namespace Automaton.Node;

public static class Program
{
    public static async Task Main()
    {
        using var host = NodePreparationHost.CreateBuilder().Build();
        await host.RunAsync();
    }
}
