using System.Net;
using System.Net.Sockets;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerRuntimeProcessPortAllocator
{
    private readonly int portStart = ReadPort("AUTOMATON_RUNTIME_PORT_START", 18000);
    private readonly int portEnd = ReadPort("AUTOMATON_RUNTIME_PORT_END", 18999);

    public int AllocatePort(int requestedPort, ISet<int> usedPorts)
    {
        var gatewayPort = RunnerPlatformPorts.GetRuntimeGatewayPort();
        if (requestedPort != gatewayPort && !usedPorts.Contains(requestedPort) && CanBind(requestedPort))
            return requestedPort;

        for (var port = portStart; port <= portEnd; port++)
        {
            if (port != gatewayPort && !usedPorts.Contains(port) && CanBind(port))
                return port;
        }

        throw new InvalidOperationException($"No runtime ports available in range {portStart}-{portEnd}.");
    }

    private static bool CanBind(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int ReadPort(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var port)
            && port is > 0 and <= 65_535
            ? port
            : fallback;
}
