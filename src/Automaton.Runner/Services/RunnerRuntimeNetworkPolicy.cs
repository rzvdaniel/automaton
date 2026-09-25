using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerRuntimeNetworkPolicy
{
    private readonly RunnerRuntimeRegistry registry;
    private readonly int portStart = ReadPort("AUTOMATON_RUNTIME_PORT_START", 18000);
    private readonly int portEnd = ReadPort("AUTOMATON_RUNTIME_PORT_END", 18999);
    private readonly string runtimeHostAddress = RunnerRuntimeEndpointConfiguration.HostAddress;
    private readonly bool canProbeRuntimeHost;

    public RunnerRuntimeNetworkPolicy(RunnerRuntimeRegistry registry)
    {
        this.registry = registry;
        canProbeRuntimeHost = IPAddress.IsLoopback(IPAddress.Parse(runtimeHostAddress));
    }

    public async Task WaitForReadinessAsync(
        int port,
        ApplicationHealthSpec health,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(health.StartupTimeoutSeconds));
        using var client = health.Path is null ? null : new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        while (!timeout.IsCancellationRequested)
        {
            if (await IsReadyAsync(port, health.Path, client, timeout.Token, cancellationToken)) return;
            if (!await DelayBeforeReadinessRetryAsync(timeout.Token, cancellationToken)) break;
        }
        throw new TimeoutException($"Runtime container did not become ready on {runtimeHostAddress}:{port}.");
    }

    public int AllocatePort(int requested)
    {
        if (requested is >= 1 and <= 65535
            && requested != RunnerPlatformPorts.GetRuntimeGatewayPort()
            && !registry.IsPortRegistered(requested)
            && CanBind(requested)) return requested;
        for (var port = portStart; port <= portEnd; port++)
        {
            if (port == RunnerPlatformPorts.GetRuntimeGatewayPort()
                || registry.IsPortRegistered(port)
                || !CanBind(port)) continue;
            return port;
        }
        throw new InvalidOperationException("No managed runtime ports are available.");
    }

    private async Task<bool> IsReadyAsync(
        int port,
        string? healthPath,
        HttpClient? client,
        CancellationToken timeoutToken,
        CancellationToken callerToken)
    {
        try
        {
            if (healthPath is null)
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(runtimeHostAddress, port, timeoutToken);
                return true;
            }

            using var response = await client!.GetAsync(
                new UriBuilder("http", runtimeHostAddress, port) { Path = healthPath }.Uri,
                timeoutToken);
            return (int)response.StatusCode is >= 200 and < 300;
        }
        catch (Exception ex) when ((ex is SocketException or HttpRequestException or TaskCanceledException)
            && !timeoutToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested && timeoutToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<bool> DelayBeforeReadinessRetryAsync(
        CancellationToken timeoutToken,
        CancellationToken callerToken)
    {
        try
        {
            await Task.Delay(100, timeoutToken);
            return true;
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested && timeoutToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private bool CanBind(int port)
    {
        if (!canProbeRuntimeHost) return true;
        try
        {
            using var listener = new TcpListener(IPAddress.Parse(runtimeHostAddress), port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int ReadPort(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
}
